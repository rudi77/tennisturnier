using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Api.Pages.Tournaments;

/// <summary>
/// Der Spielplanvorschlag (ADR-0002): rechnen, ansehen, übernehmen.
///
/// Der Vorschlag wird nicht beim Laden der Seite gerechnet. Ein Solverlauf, der
/// schon durch das Öffnen einer Seite ausgelöst wird, macht aus einer Ansicht
/// eine Handlung — und die Turnierleitung sieht Zahlen, die sie nicht angefordert
/// hat.
/// </summary>
[Authorize]
public sealed class ScheduleModel : AppPageModel
{
    private readonly ITournamentService _tournaments;
    private readonly ISchedulingService _scheduling;

    public ScheduleModel(ITournamentService tournaments, ISchedulingService scheduling)
    {
        _tournaments = tournaments;
        _scheduling = scheduling;
    }

    public Guid Id { get; private set; }

    public TournamentDetail Tournament { get; private set; } = null!;

    public SchedulePlanResult? Plan { get; private set; }

    /// <summary>Wurde der Plan übernommen? Dann ist er kein Vorschlag mehr.</summary>
    public bool Confirmed { get; private set; }

    public TournamentNav Nav => new(Tournament, "/schedule");

    public async Task<IActionResult> OnGetAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        return await TryAsync(() => LoadAsync(cancellationToken)) ? Page() : NotFound();
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Tournament = await _tournaments.GetAsync(Id, cancellationToken);

    public async Task<IActionResult> OnPostProposeAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var computed = await TryAsync(async () =>
        {
            await LoadAsync(cancellationToken);
            Plan = await _scheduling.ProposeAsync(tournamentId, cancellationToken);
        });

        return computed ? Partial("_Plan", this) : ErrorFragment();
    }

    /// <summary>
    /// Übernimmt genau die Ansetzungen, die im Vorschlag standen.
    ///
    /// Sie kommen als gleich lange Felder zurück statt als verschachteltes
    /// Objekt: die Zuordnung ist dann die Position, und ein fehlendes Feld fällt
    /// sofort auf, statt still eine Ansetzung mit halben Angaben zu bilden.
    /// </summary>
    public async Task<IActionResult> OnPostConfirmAsync(
        Guid tournamentId,
        Guid[] matchId,
        Guid[] courtId,
        int[] sequenceOnCourt,
        DateTimeOffset[] plannedStart,
        TimeSpan[] estimatedDuration,
        CancellationToken cancellationToken)
    {
        Id = tournamentId;

        var confirmed = await TryAsync(async () =>
        {
            if (matchId.Length != courtId.Length
                || matchId.Length != sequenceOnCourt.Length
                || matchId.Length != plannedStart.Length
                || matchId.Length != estimatedDuration.Length)
            {
                throw new DomainException("Der Vorschlag ist unvollständig angekommen. Bitte neu rechnen.");
            }

            var assignments = matchId
                .Select((id, i) => new ConfirmedAssignment(
                    id, courtId[i], sequenceOnCourt[i], plannedStart[i], estimatedDuration[i]))
                .ToList();

            await LoadAsync(cancellationToken);

            Plan = await _scheduling.ConfirmAsync(
                tournamentId, new ConfirmScheduleRequest(assignments), cancellationToken);

            Confirmed = true;
        });

        return confirmed ? Partial("_Plan", this) : ErrorFragment();
    }
}
