using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Pages.Tournaments;

/// <summary>
/// Stammdaten, Zustandsübergänge und Meldeliste eines Turniers.
/// </summary>
[Authorize]
public sealed class DetailModel : AppPageModel
{
    private readonly ITournamentService _tournaments;
    private readonly IPlayerService _players;

    public DetailModel(ITournamentService tournaments, IPlayerService players)
    {
        _tournaments = tournaments;
        _players = players;
    }

    public Guid Id { get; private set; }

    public TournamentDetail Tournament { get; private set; } = null!;

    public IReadOnlyList<PlayerSummary> Hits { get; private set; } = [];

    public TournamentNav Nav => new(Tournament, string.Empty);

    /// <summary>
    /// Die Übergänge, die im aktuellen Zustand überhaupt in Frage kommen.
    ///
    /// Die Domäne weist einen unpassenden Übergang ohnehin ab; eine Schaltfläche,
    /// die es dennoch anbietet, ist trotzdem falsch — sie behauptet eine
    /// Möglichkeit, die es nicht gibt.
    /// </summary>
    public IReadOnlyList<(string Handler, string Label, string Style)> Transitions =>
        Tournament.State switch
        {
            TournamentState.Draft =>
                [("OpenRegistration", "Meldung öffnen", "primary")],
            TournamentState.RegistrationOpen =>
                [("CloseRegistration", "Meldeschluss", "primary"), ("Abandon", "Absagen", "danger")],
            TournamentState.RegistrationClosed =>
            [
                ("Draw", "Auslosen", "primary"),
                ("ReopenRegistration", "Meldung wieder öffnen", ""),
                ("Abandon", "Absagen", "danger"),
            ],
            TournamentState.DrawGenerated =>
            [
                ("Start", "Turnier starten", "go"),
                ("ReopenRegistration", "Auslosung zurücknehmen", ""),
                ("Abandon", "Absagen", "danger"),
            ],
            TournamentState.InProgress => Tournament.SchedulingMode == SchedulingMode.Planning
                ?
                [
                    ("MatchDay", "Auf Turniertag umstellen", "go"),
                    ("Complete", "Abschließen", ""),
                    ("Abandon", "Abbrechen", "danger"),
                ]
                :
                [
                    ("Planning", "Zurück in die Planung", ""),
                    ("Complete", "Abschließen", ""),
                    ("Abandon", "Abbrechen", "danger"),
                ],
            _ => [],
        };

    /// <summary>Solange nicht ausgelost ist, lässt sich die Meldeliste noch ändern.</summary>
    public bool EntriesEditable =>
        Tournament.State is TournamentState.Draft
            or TournamentState.RegistrationOpen
            or TournamentState.RegistrationClosed;

    public async Task<IActionResult> OnGetAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(() => LoadAsync(cancellationToken)) ? Page() : NotFound();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Tournament = await _tournaments.GetAsync(Id, cancellationToken);

    // --- Stammdaten und Zustand ---------------------------------------------

    public Task<IActionResult> OnPostUpdateAsync(
        Guid tournamentId,
        string name,
        DateOnly startsOn,
        DateOnly endsOn,
        CancellationToken cancellationToken) =>
        HeaderAsync(
            tournamentId,
            () => _tournaments.UpdateAsync(
                tournamentId, new UpdateTournamentRequest(name, startsOn, endsOn), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Ein einziger Endpunkt für alle Übergänge — die Handlung steht im Feld
    /// <c>transition</c>. Neun fast gleiche Handler wären neun Stellen, an denen
    /// dasselbe Neuladen danach steht.
    /// </summary>
    public async Task<IActionResult> OnPostTransitionAsync(
        Guid tournamentId,
        string transition,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        Func<Task>? action = transition switch
        {
            "OpenRegistration" => () => _tournaments.OpenRegistrationAsync(tournamentId, cancellationToken),
            "CloseRegistration" => () => _tournaments.CloseRegistrationAsync(tournamentId, cancellationToken),
            "ReopenRegistration" => () => _tournaments.ReopenRegistrationAsync(tournamentId, cancellationToken),
            "Draw" => () => _tournaments.GenerateDrawAsync(tournamentId, cancellationToken),
            "Start" => () => _tournaments.StartAsync(tournamentId, cancellationToken),
            "Complete" => () => _tournaments.CompleteAsync(tournamentId, cancellationToken),
            "Abandon" => () => _tournaments.AbandonAsync(tournamentId, cancellationToken),
            "MatchDay" => () => _tournaments.SwitchToMatchDayAsync(tournamentId, cancellationToken),
            "Planning" => () => _tournaments.SwitchToPlanningAsync(tournamentId, cancellationToken),
            _ => null,
        };

        if (action is null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Partial("_Toast", $"Unbekannter Übergang „{transition}\".");
        }

        return await HeaderAsync(tournamentId, action, cancellationToken);
    }

    // --- Meldungen ----------------------------------------------------------

    /// <summary>Die Spielersuche für das Meldeformular.</summary>
    public async Task<IActionResult> OnGetSearchAsync(string? q, CancellationToken cancellationToken)
    {
        Hits = string.IsNullOrWhiteSpace(q)
            ? []
            : await _players.SearchAsync(q, 10, cancellationToken);

        return Partial("_PlayerHits", this);
    }

    public async Task<IActionResult> OnPostCreatePlayerAsync(
        Guid tournamentId,
        string firstName,
        string lastName,
        string? email,
        string? phone,
        DateOnly? dateOfBirth,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var created = await TryAsync(() => _players.CreatePlayerAsync(
            new CreatePlayerRequest(firstName, lastName, email, phone, dateOfBirth), cancellationToken));

        if (!created)
        {
            return ErrorFragment();
        }

        // Direkt in die Trefferliste: der neue Spieler ist fast immer der, der
        // gleich gemeldet werden soll.
        Hits = await _players.SearchAsync(lastName, 10, cancellationToken);

        return Partial("_PlayerHits", this);
    }

    public async Task<IActionResult> OnPostEnterAsync(
        Guid tournamentId,
        Guid firstPlayerId,
        Guid? secondPlayerId,
        int? seed,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var entered = await TryAsync(async () =>
        {
            var participant = await _players.CreateParticipantAsync(
                new CreateParticipantRequest(firstPlayerId, secondPlayerId), cancellationToken);

            await _tournaments.EnterAsync(
                tournamentId, new EnterTournamentRequest(participant.Id, seed), cancellationToken);
        });

        return entered ? await EntriesAsync(cancellationToken) : ErrorFragment();
    }

    public Task<IActionResult> OnPostAcceptAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken) =>
        EntryActionAsync(tournamentId, () => _tournaments.AcceptAsync(tournamentId, entryId, cancellationToken), cancellationToken);

    /// <summary>
    /// Nimmt alle offenen Meldungen an.
    ///
    /// Im Vereinsturnier ist das der Normalfall — die Meldeliste ist die
    /// Teilnehmerliste. Ohne diesen Weg klickt die Turnierleitung bei 32
    /// Meldungen 32-mal, bevor sie auslosen kann.
    /// </summary>
    public Task<IActionResult> OnPostAcceptAllAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        EntryActionAsync(tournamentId, async () =>
        {
            var tournament = await _tournaments.GetAsync(tournamentId, cancellationToken);

            foreach (var entry in tournament.Entries.Where(e => e.Status == EntryStatus.Applied))
            {
                await _tournaments.AcceptAsync(tournamentId, entry.Id, cancellationToken);
            }
        }, cancellationToken);

    public Task<IActionResult> OnPostWaitingListAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken) =>
        EntryActionAsync(tournamentId, () => _tournaments.MoveToWaitingListAsync(tournamentId, entryId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostWithdrawAsync(Guid tournamentId, Guid entryId, CancellationToken cancellationToken) =>
        EntryActionAsync(tournamentId, () => _tournaments.WithdrawAsync(tournamentId, entryId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostSeedAsync(
        Guid tournamentId,
        Guid entryId,
        int? seed,
        CancellationToken cancellationToken) =>
        EntryActionAsync(
            tournamentId,
            () => _tournaments.SetSeedAsync(tournamentId, entryId, new SetSeedRequest(seed), cancellationToken),
            cancellationToken);

    private async Task<IActionResult> EntryActionAsync(
        Guid tournamentId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(action) ? await EntriesAsync(cancellationToken) : ErrorFragment();
    }

    private async Task<IActionResult> EntriesAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        return Partial("_Entries", this);
    }

    /// <summary>
    /// Nach einem Zustandsübergang wird der ganze Seiteninhalt getauscht: der
    /// Übergang ändert Kopf, mögliche Handlungen und die Bearbeitbarkeit der
    /// Meldungen zugleich.
    /// </summary>
    private async Task<IActionResult> HeaderAsync(
        Guid tournamentId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        if (!await TryAsync(action))
        {
            return ErrorFragment();
        }

        await LoadAsync(cancellationToken);

        return Partial("_Body", this);
    }
}
