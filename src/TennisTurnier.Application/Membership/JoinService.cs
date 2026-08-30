using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Social;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Players;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Application.Membership;

public interface IJoinService
{
    Task<JoinView> GetAsync(string token, CancellationToken cancellationToken = default);

    Task<JoinResult> JoinAsync(
        string token,
        JoinRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Einem Turnier beitreten — der Weg, den ein geteilter Link öffnet.
///
/// Er ersetzt die anonyme Selbstmeldung (ADR-0010, abgelöst durch ADR-0012).
/// Der Unterschied ist nicht der Link, sondern was danach bleibt: früher
/// hinterließ ein Fremder Namen und Adresse und bekam einen Bestätigungscode
/// als einzigen Weg zurück. Jetzt wird er Mitglied — er findet das Turnier
/// unter seinen eigenen, sieht den Spielplan und bleibt erreichbar. Der Code
/// entfällt, weil das Konto der Weg zurück ist.
///
/// Der Link bleibt die Eintrittskarte: wer ihn hat, darf herein, ohne dass ihn
/// jemand einzeln freischaltet. Das ist die Regel einer Gruppe, nicht die
/// eines Vereinsregisters.
///
/// Vier Regeln tragen den Ablauf, drei davon geerbt vom Vorgänger:
///
///  1. Das Turnier kommt <em>ausschließlich</em> über
///     <see cref="ITournamentRepository.FindByRegistrationTokenAsync"/>. Der
///     normale Weg liefe in den Query-Filter, und der Beitretende hat noch
///     keine Rolle — er sähe sein Ziel nicht.
///  2. Kein Neuaufbau der öffentlichen Projektion; er lädt das Turnier über
///     denselben Filter und scheiterte aus demselben Grund.
///  3. Kein <c>FlushAsync</c>: es leert den Änderungsverfolger, und die eben
///     angelegten Spieler wären nicht mehr Teil derselben Arbeitseinheit. Genau
///     ein <c>SaveChangesAsync</c>, am Ende.
///  4. Die Mitgliedschaft steht am Ende, nach jeder Prüfung. Eine abgewiesene
///     Meldung soll niemanden zum Mitglied machen — sonst gehörte man einer
///     Gruppe an, weil man sich vertippt hat.
/// </summary>
public sealed class JoinService : IJoinService
{
    private readonly ITournamentRepository _tournaments;
    private readonly IPlayerRepository _players;
    private readonly ParticipantResolver _participants;
    private readonly IRoleAssignmentRepository _roles;
    private readonly IUserDirectory _directory;
    private readonly IUserContext _userContext;
    private readonly FeedRecorder _feed;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public JoinService(
        ITournamentRepository tournaments,
        IPlayerRepository players,
        ParticipantResolver participants,
        IRoleAssignmentRepository roles,
        IUserDirectory directory,
        IUserContext userContext,
        FeedRecorder feed,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _tournaments = tournaments;
        _players = players;
        _participants = participants;
        _roles = roles;
        _directory = directory;
        _userContext = userContext;
        _feed = feed;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<JoinView> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(token, cancellationToken);

        return new JoinView(
            tournament.Id,
            tournament.Name,
            tournament.Venue.Name,
            tournament.Venue.City,
            tournament.StartsOn,
            tournament.EndsOn,
            tournament.Discipline,
            tournament.NeedsPartnerOnEntry,
            IsOpen(tournament),
            FreeSlots(tournament),
            tournament.Registration.Deadline,
            _userContext.Current.TournamentIds.Contains(tournament.Id));
    }

    public async Task<JoinResult> JoinAsync(
        string token,
        JoinRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tournament = await LoadAsync(token, cancellationToken);
        var account = await AccountAsync(cancellationToken);

        // Beitreten geht immer, melden nur bei offener Meldung. Das ist der
        // Kern der Gruppe: der Meldeschluss beendet die Teilnehmerliste, nicht
        // die Zugehörigkeit — wer danach dazukommt, sieht den Spielplan.
        var entry = request.Play && IsOpen(tournament)
            ? await EnterAsync(tournament, request, account, cancellationToken)
            : null;

        var neu = await GrantMembershipAsync(tournament.Id, account.Id, cancellationToken);

        // Nur beim ersten Mal: derselbe Link ein zweites Mal ist kein zweiter
        // Beitritt und keine zweite Meldung im Feed (ADR-0014).
        if (neu)
        {
            _feed.Record(
                tournament.Id,
                PostKind.Joined,
                entry is null
                    ? $"{account.PreferredName} gehört jetzt dazu."
                    : $"{account.PreferredName} ist dabei — und spielt mit.");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new JoinResult(tournament.Id, entry?.Id, entry?.Status);
    }

    /// <summary>
    /// Die Meldung selbst — bis auf den Bestätigungscode dieselbe wie zuvor.
    /// </summary>
    private async Task<TournamentEntry> EnterAsync(
        Tournament tournament,
        JoinRequest request,
        UserAccount account,
        CancellationToken cancellationToken)
    {
        var hasPartner = HasPartner(request);
        tournament.RequireMatchesDiscipline(hasPartner);

        var self = await ResolveSelfAsync(request, account, cancellationToken);

        // Der Partner wird genannt, nicht angemeldet. Er ist zunächst ein
        // Spieler ohne Konto und wird zu einem, sobald er selbst beitritt —
        // die Auflösung findet ihn dann über Namen und Adresse wieder. Ein
        // Doppel, das auf die Bestätigung des Partners wartet, wäre ein
        // eigener Zustand mit eigenem Verfall; er ist nicht gebaut (ADR-0012).
        var partner = hasPartner
            ? await _participants.ResolveAsync(
                request.PartnerFirstName!,
                request.PartnerLastName!,
                request.PartnerEmail,
                phone: null,
                cancellationToken)
            : null;

        // Idempotenz vor Kapazität: der Doppelklick auf „Absenden" darf nicht
        // beim zweiten Mal auf der Warteliste landen, nur weil der erste das
        // Feld gerade vollgemacht hat.
        var lineups = await _participants.LoadLineupsAsync(tournament, cancellationToken);

        if (lineups.Find(self, partner) is { } existing)
        {
            return existing;
        }

        var participant = _participants.CreateParticipant(self, partner, request.TeamName);

        // Ab hier ist die Meldung beschlossen — was bis dahin entstand, wartete.
        _participants.Commit();

        // Vor dem Melden gezählt: danach zählt die eigene Meldung mit, und ein
        // Feld mit genau einem freien Platz wäre plötzlich voll.
        var full = tournament.Registration.IsFull(tournament.CountAgainstCapacity());

        var entry = tournament.Enter(
            Guid.NewGuid(),
            participant.Id,
            seed: null,
            origin: EntryOrigin.SelfService,
            registeredAt: _clock.Now);

        // Bei erschöpfter Kapazität entsteht die Meldung als Warteliste statt
        // als Fehler. Für den Melder ist das die bessere Antwort — und die
        // Turnierleitung entscheidet ohnehin, wer nachrückt.
        if (full)
        {
            tournament.MoveToWaitingList(entry.Id);
        }

        return entry;
    }

    /// <summary>
    /// Der Spieler zum angemeldeten Konto.
    ///
    /// Erst über die Verbindung, die beim letzten Mal entstanden ist — wer
    /// schon einmal mitgespielt hat, ist derselbe Spieler und nicht ein
    /// zweiter mit gleichem Namen. Erst wenn es keine gibt, wird über Namen
    /// und Konto-Adresse gesucht; findet das jemanden, den die Turnierleitung
    /// aus einer Liste eingelesen hat, wird er von jetzt an dieses Konto
    /// haben.
    /// </summary>
    private async Task<Player> ResolveSelfAsync(
        JoinRequest request,
        UserAccount account,
        CancellationToken cancellationToken)
    {
        if (await _players.FindByUserAccountAsync(account.Id, cancellationToken) is { } known)
        {
            return known;
        }

        if (string.IsNullOrWhiteSpace(request.FirstName)
            || string.IsNullOrWhiteSpace(request.LastName))
        {
            throw new DomainException("Zum Mitspielen fehlen Vor- und Nachname.");
        }

        var self = await _participants.ResolveAsync(
            request.FirstName,
            request.LastName,
            account.Email,
            request.Phone,
            cancellationToken);

        self.LinkAccount(account.Id);

        return self;
    }

    /// <summary>
    /// Die Mitgliedschaft — idempotent, denn derselbe Link ein zweites Mal ist
    /// kein zweiter Beitritt. Wer schon eine andere Rolle am Turnier hat,
    /// braucht keine: die Turnierleitung ist kein Mitglied zweiter Klasse.
    /// </summary>
    private async Task<bool> GrantMembershipAsync(
        Guid tournamentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existing = await _roles.ListByTournamentAsync(tournamentId, cancellationToken);

        if (existing.Any(a => a.UserId == userId))
        {
            return false;
        }

        _roles.Add(new RoleAssignment(
            Guid.NewGuid(), userId, Role.Member, ResourceScope.Tournament(tournamentId)));

        return true;
    }


    /// <summary>
    /// Das Konto des Aufrufers.
    ///
    /// Wer hier ankommt, ist angemeldet — der Endpunkt verlangt es — und wer
    /// angemeldet ist, hat ein Konto: die Benutzerauflösung legt es an, bevor
    /// irgendein Anwendungsfall zum Zug kommt (ADR-0007). Eine Ausweichfassung
    /// für den Fall, dass keines da ist, wäre eine, die nie läuft.
    /// </summary>
    private async Task<UserAccount> AccountAsync(CancellationToken cancellationToken) =>
        (await _directory.FindAsync(_userContext.Current.UserId, cancellationToken))!;

    /// <summary>
    /// Der Tokenweg. Ein unbekanntes Token und ein Turnier, über das gerade
    /// nichts geht, sind von außen nicht zu unterscheiden — sonst wäre der
    /// Endpunkt ein Orakel dafür, welche Token es gibt.
    /// </summary>
    private async Task<Tournament> LoadAsync(string token, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new NotFoundException("Beitrittslink")
            : await _tournaments.FindByRegistrationTokenAsync(token.Trim(), cancellationToken)
              ?? throw new NotFoundException("Beitrittslink");

    private bool IsOpen(Tournament tournament) =>
        tournament.State == TournamentState.RegistrationOpen
        && !tournament.Registration.IsPastDeadline(_clock.Now);

    private static int? FreeSlots(Tournament tournament) =>
        tournament.Registration.Capacity is { } capacity
            ? Math.Max(0, capacity - tournament.CountAgainstCapacity())
            : null;

    private static bool HasPartner(JoinRequest request) =>
        !string.IsNullOrWhiteSpace(request.PartnerFirstName)
        || !string.IsNullOrWhiteSpace(request.PartnerLastName);
}
