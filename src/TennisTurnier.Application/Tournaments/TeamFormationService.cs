using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Tournaments;

/// <summary>Zwei Meldungen, die zusammen antreten.</summary>
/// <param name="TeamName">
/// Der Name, unter dem das Paar antritt — freiwillig. Ohne ihn heißt es nach
/// seinen beiden Meldungen.
/// </param>
public sealed record FormTeamRequest(Guid FirstEntryId, Guid SecondEntryId, string? TeamName = null);

/// <param name="Formed">Wie viele Teams entstanden sind.</param>
/// <param name="LeftOver">
/// Wie viele Meldungen ohne Team geblieben sind — bei ungerader Zahl genau
/// eine. Sie steht danach immer noch im Feld, und das Auslosen des Draws weist
/// sie ab: die Turnierleitung muss entscheiden, ob sie auf die Warteliste
/// kommt, jemanden mitbringt oder ein Dreierteam bekommt, das es hier nicht
/// gibt.
/// </param>
public sealed record DrawTeamsResult(int Formed, int LeftOver);

/// <summary>
/// Teams für ein Doppel, dessen Paare die Turnierleitung bildet.
///
/// Eigener Anwendungsfall und nicht Teil von <see cref="TournamentService"/>:
/// er ist der einzige, der Meldungen und Teilnehmer gemeinsam anfasst — ein
/// Team ist beides zugleich, eine neue Meldung im Turnier und ein neuer
/// Teilnehmer daneben (ADR-0008).
/// </summary>
public interface ITeamFormationService
{
    /// <summary>Stellt zwei Meldungen von Hand zusammen. Liefert die Meldung des Teams.</summary>
    Task<Guid> FormAsync(
        Guid tournamentId,
        FormTeamRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lost alle Meldungen ohne Team paarweise aus.</summary>
    Task<DrawTeamsResult> DrawAsync(Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Löst ein Team wieder auf.</summary>
    Task DisbandAsync(
        Guid tournamentId,
        Guid teamEntryId,
        CancellationToken cancellationToken = default);
}

public sealed class TeamFormationService : ITeamFormationService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IPlayerRepository _players;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;
    private readonly TournamentOptions _options;

    public TeamFormationService(
        ITournamentRepository tournaments,
        IPlayerRepository players,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext,
        TournamentOptions options)
    {
        _tournaments = tournaments;
        _players = players;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
        _options = options;
    }

    public async Task<Guid> FormAsync(
        Guid tournamentId,
        FormTeamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        var byId = await ParticipantsOfAsync(tournament, cancellationToken);

        var teamEntryId = Pair(
            tournament, byId, request.FirstEntryId, request.SecondEntryId, request.TeamName);

        await SaveAsync(tournamentId, cancellationToken);

        return teamEntryId;
    }

    /// <summary>
    /// Das Los. Es liegt hier und nicht in der Domäne: sie rechnet, und was sie
    /// rechnet, muss zweimal dasselbe ergeben.
    ///
    /// Gelost wird über eine feste Ausgangsreihenfolge — nach Meldezeitpunkt,
    /// bei Gleichstand nach Kennung. Ohne sie hinge das Ergebnis daran, in
    /// welcher Reihenfolge die Datenbank die Meldungen zurückgibt, und ein
    /// gesetzter Saatwert wäre wertlos.
    /// </summary>
    public async Task<DrawTeamsResult> DrawAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);
        tournament.RequireFormsTeamsItself();

        var byId = await ParticipantsOfAsync(tournament, cancellationToken);

        var offen = tournament.UnpairedEntries
            .OrderBy(entry => entry.RegisteredAt)
            .ThenBy(entry => entry.Id)
            .ToArray();

        Los().Shuffle(offen);

        var gebildet = 0;

        for (var i = 0; i + 1 < offen.Length; i += 2)
        {
            Pair(tournament, byId, offen[i].Id, offen[i + 1].Id, teamName: null);
            gebildet++;
        }

        await SaveAsync(tournamentId, cancellationToken);

        return new DrawTeamsResult(gebildet, offen.Length - (gebildet * 2));
    }

    public async Task DisbandAsync(
        Guid tournamentId,
        Guid teamEntryId,
        CancellationToken cancellationToken = default)
    {
        var tournament = await LoadForManagement(tournamentId, cancellationToken);

        tournament.DisbandTeam(teamEntryId);

        await SaveAsync(tournamentId, cancellationToken);
    }

    /// <summary>
    /// Das Los für diese Auslosung.
    ///
    /// Ohne Saatwert der geteilte Zufall. Mit einem — <c>Tournament:TeamDrawSeed</c>
    /// — ein eigener je Auslosung und nicht ein fortlaufender: sonst hinge das
    /// Ergebnis daran, wie oft seit dem Start gelost wurde, und dieselbe
    /// Meldungsliste ergäbe zweimal etwas anderes.
    /// </summary>
    private Random Los() => _options.TeamDrawSeed is { } saat ? new Random(saat) : Random.Shared;

    /// <summary>
    /// Stellt ein Team zusammen: erst der Teilnehmer, dann die Meldung.
    ///
    /// Der Teilnehmer wird angelegt, bevor das Aggregat ihn kennt — aber erst
    /// gespeichert, nachdem es ihn angenommen hat. Weist es die Zusammenstellung
    /// ab, bleibt nichts zurück.
    /// </summary>
    private Guid Pair(
        Tournament tournament,
        IReadOnlyDictionary<Guid, Participant> byId,
        Guid firstEntryId,
        Guid secondEntryId,
        string? teamName)
    {
        var first = ParticipantOf(tournament, byId, firstEntryId);
        var second = ParticipantOf(tournament, byId, secondEntryId);

        var team = Participant.Team(
            Guid.NewGuid(),
            // Genau ein Spieler je Meldung: ein Turnier, das seine Teams selbst
            // bildet, nimmt keine Paarmeldung an — das weist
            // Tournament.RequireMatchesDiscipline vorher ab.
            first.PlayerIds[0],
            second.PlayerIds[0],
            DisplayName(teamName, first, second));

        var entry = tournament.FormTeam(Guid.NewGuid(), team.Id, firstEntryId, secondEntryId);

        _players.Add(team);

        return entry.Id;
    }

    /// <summary>
    /// „Berger, Anna / Huber, Eva" — und mit eigenem Namen davor, durch einen
    /// Mittelpunkt getrennt. Dieselbe Form wie bei der Paarmeldung, damit im
    /// Aushang nicht zweierlei steht.
    /// </summary>
    private static string DisplayName(
        string? teamName,
        Participant first,
        Participant second)
    {
        var beide = $"{first.DisplayName} / {second.DisplayName}";

        return string.IsNullOrWhiteSpace(teamName) ? beide : $"{teamName.Trim()} · {beide}";
    }

    private static Participant ParticipantOf(
        Tournament tournament,
        IReadOnlyDictionary<Guid, Participant> byId,
        Guid entryId)
    {
        var entry = tournament.Entries.FirstOrDefault(e => e.Id == entryId)
            ?? throw new NotFoundException("Meldung", entryId);

        // Zu jeder Meldung gibt es ihren Teilnehmer — der Fremdschlüssel lässt
        // keine ohne zu.
        return byId[entry.ParticipantId];
    }

    private async Task<IReadOnlyDictionary<Guid, Participant>> ParticipantsOfAsync(
        Tournament tournament,
        CancellationToken cancellationToken)
    {
        var participants = await _players.FindParticipantsAsync(
            [.. tournament.Entries.Select(entry => entry.ParticipantId)], cancellationToken);

        return participants.ToDictionary(participant => participant.Id);
    }

    private async Task<Tournament> LoadForManagement(
        Guid tournamentId,
        CancellationToken cancellationToken)
    {
        var tournament = await _tournaments.FindAsync(tournamentId, cancellationToken)
            ?? throw new NotFoundException("Turnier", tournamentId);

        _userContext.Current.Require(
            Permission.ManageTournament, ResourceScope.Tournament(tournament.Id));

        return tournament;
    }

    /// <summary>
    /// Wie jede Änderung am Feld: erst schreiben, dann die öffentliche Ansicht
    /// nachziehen. Vor der Auslosung gibt es noch keine — und genau dort steht
    /// die Teambildung. Der Aufruf bleibt trotzdem stehen, damit hier nicht die
    /// eine Stelle entsteht, an der er fehlt.
    /// </summary>
    private async Task SaveAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        await _unitOfWork.FlushAsync(cancellationToken);
        await _publicView.RebuildAsync(tournamentId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
