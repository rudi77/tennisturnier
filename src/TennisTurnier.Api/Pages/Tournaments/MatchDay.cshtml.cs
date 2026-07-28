using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Api.Pages.Tournaments;

/// <summary>
/// Das Platzbrett des Turniertags (ADR-0002).
///
/// Was hier steht, ist eine Reihenfolge und keine Uhrzeit: aufrufen, anpfeifen,
/// abschließen. Die geschätzten Zeiten stehen dabei, aber sie sind Auskunft,
/// nicht Plan.
/// </summary>
[Authorize]
public sealed class MatchDayModel : AppPageModel
{
    private readonly ITournamentService _tournaments;
    private readonly ICourtQueueService _queues;

    public MatchDayModel(ITournamentService tournaments, ICourtQueueService queues)
    {
        _tournaments = tournaments;
        _queues = queues;
    }

    public Guid Id { get; private set; }

    public TournamentDetail Tournament { get; private set; } = null!;

    public IReadOnlyList<CourtBoard> Boards { get; private set; } = [];

    public TournamentNav Nav => new(Tournament, "/matchday");

    public async Task<IActionResult> OnGetAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(() => LoadAsync(cancellationToken)) ? Page() : NotFound();
    }

    /// <summary>Nur das Brett — dafür fragt die Seite im Sekundentakt nach.</summary>
    public async Task<IActionResult> OnGetBoardAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(() => LoadAsync(cancellationToken))
            ? Partial("_Board", this)
            : ErrorFragment();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Tournament = await _tournaments.GetAsync(Id, cancellationToken);
        Boards = await _queues.GetBoardAsync(Id, cancellationToken);
    }

    public Task<IActionResult> OnPostCallAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken) =>
        BoardAsync(tournamentId, () => _queues.CallAsync(assignmentId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostStartAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken) =>
        BoardAsync(tournamentId, () => _queues.StartAsync(assignmentId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostFinishAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken) =>
        BoardAsync(tournamentId, () => _queues.FinishAsync(assignmentId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostSuspendAsync(Guid tournamentId, Guid assignmentId, CancellationToken cancellationToken) =>
        BoardAsync(tournamentId, () => _queues.SuspendAsync(assignmentId, cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostResumeAsync(
        Guid tournamentId,
        Guid assignmentId,
        Guid? courtId,
        CancellationToken cancellationToken) =>
        BoardAsync(
            tournamentId,
            () => _queues.ResumeAsync(assignmentId, new ResumeMatchRequest(courtId), cancellationToken),
            cancellationToken);

    public Task<IActionResult> OnPostPromiseAsync(
        Guid tournamentId,
        Guid assignmentId,
        DateTimeOffset earliestStart,
        CancellationToken cancellationToken) =>
        BoardAsync(
            tournamentId,
            () => _queues.PromiseAsync(assignmentId, new PromiseStartRequest(earliestStart), cancellationToken),
            cancellationToken);

    /// <summary>
    /// Verschiebt ein wartendes Match um einen Platz in der Schlange.
    ///
    /// Die neue Reihenfolge entsteht hier und nicht im Browser: der Anwendungsfall
    /// will die vollständige Liste, und die aus einem gerade angezeigten Stand zu
    /// bilden hieße, eine zwischenzeitliche Änderung zu überschreiben.
    /// </summary>
    public Task<IActionResult> OnPostMoveAsync(
        Guid tournamentId,
        Guid courtId,
        Guid assignmentId,
        int direction,
        CancellationToken cancellationToken) =>
        BoardAsync(tournamentId, async () =>
        {
            var boards = await _queues.GetBoardAsync(tournamentId, cancellationToken);
            var board = boards.FirstOrDefault(b => b.CourtId == courtId)
                ?? throw new DomainException("Der Platz gehört nicht zu diesem Turnier.");

            var order = board.Queue.Select(q => q.AssignmentId).ToList();
            var from = order.IndexOf(assignmentId);
            var to = from + direction;

            if (from < 0 || to < 0 || to >= order.Count)
            {
                throw new DomainException("Das Match steht bereits am Rand der Warteschlange.");
            }

            (order[from], order[to]) = (order[to], order[from]);

            await _queues.ReorderAsync(tournamentId, courtId, new ReorderQueueRequest(order), cancellationToken);
        }, cancellationToken);

    private async Task<IActionResult> BoardAsync(
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

        return Partial("_Board", this);
    }
}
