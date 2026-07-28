using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TennisTurnier.Api.Web;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;

namespace TennisTurnier.Api.Pages.Clubs;

/// <summary>
/// Ein Verein mit seinen Plätzen und Turnieren.
///
/// Plätze und Turniere stehen auf einer Seite, weil sie zusammen gepflegt
/// werden: wer ein Turnier ansetzt, will wissen, wie viele Plätze wann offen
/// sind.
/// </summary>
[Authorize]
public sealed class DetailModel : AppPageModel
{
    private readonly IClubService _clubs;
    private readonly ITournamentService _tournaments;
    private readonly IFormatTemplateService _formats;

    public DetailModel(IClubService clubs, ITournamentService tournaments, IFormatTemplateService formats)
    {
        _clubs = clubs;
        _tournaments = tournaments;
        _formats = formats;
    }

    public Guid ClubId { get; private set; }

    public ClubDetail Club { get; private set; } = null!;

    public IReadOnlyList<TournamentSummary> Tournaments { get; private set; } = [];

    public IReadOnlyList<FormatTemplateSummary> Formats { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid clubId, CancellationToken cancellationToken)
    {
        ClubId = clubId;

        if (!await TryAsync(() => LoadAsync(cancellationToken)))
        {
            return NotFound();
        }

        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Club = await _clubs.GetAsync(ClubId, cancellationToken);
        Tournaments = await _tournaments.ListAsync(ClubId, cancellationToken);
        Formats = await _formats.ListAsync(ClubId, cancellationToken);
    }

    // --- Plätze -------------------------------------------------------------

    public Task<IActionResult> OnPostAddCourtAsync(
        Guid clubId,
        string name,
        CourtSurface surface,
        CourtLocation location,
        bool isCenterCourt,
        CancellationToken cancellationToken) =>
        CourtsAsync(clubId, () => _clubs.AddCourtAsync(
            clubId, new CreateCourtRequest(name, surface, location, isCenterCourt), cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostUpdateCourtAsync(
        Guid clubId,
        Guid courtId,
        string name,
        bool isCenterCourt,
        bool isActive,
        CancellationToken cancellationToken) =>
        CourtsAsync(clubId, () => _clubs.UpdateCourtAsync(
            clubId, courtId, new UpdateCourtRequest(name, isCenterCourt, isActive), cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostAddAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        DayOfWeek dayOfWeek,
        TimeOnly opensAt,
        TimeOnly closesAt,
        DateOnly validFrom,
        DateOnly? validUntil,
        CancellationToken cancellationToken) =>
        CourtsAsync(clubId, () => _clubs.AddAvailabilityAsync(
            clubId,
            courtId,
            new CreateAvailabilityRequest(dayOfWeek, opensAt, closesAt, validFrom, validUntil),
            cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostRemoveAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        Guid windowId,
        CancellationToken cancellationToken) =>
        CourtsAsync(
            clubId,
            () => _clubs.RemoveAvailabilityAsync(clubId, courtId, windowId, cancellationToken),
            cancellationToken);

    public Task<IActionResult> OnPostAddBlockAsync(
        Guid clubId,
        Guid courtId,
        DateTimeOffset from,
        DateTimeOffset to,
        BlockReason reason,
        string? note,
        CancellationToken cancellationToken) =>
        CourtsAsync(clubId, () => _clubs.AddBlockAsync(
            clubId, courtId, new CreateBlockRequest(from, to, reason, note), cancellationToken), cancellationToken);

    public Task<IActionResult> OnPostRemoveBlockAsync(
        Guid clubId,
        Guid courtId,
        Guid blockId,
        CancellationToken cancellationToken) =>
        CourtsAsync(
            clubId,
            () => _clubs.RemoveBlockAsync(clubId, courtId, blockId, cancellationToken),
            cancellationToken);

    // --- Turniere -----------------------------------------------------------

    public async Task<IActionResult> OnPostCreateTournamentAsync(
        Guid clubId,
        string name,
        DateOnly startsOn,
        DateOnly endsOn,
        Guid formatTemplateId,
        CancellationToken cancellationToken)
    {
        ClubId = clubId;

        var created = await TryAsync(() => _tournaments.CreateAsync(
            clubId, new CreateTournamentRequest(name, startsOn, endsOn, formatTemplateId), cancellationToken));

        if (!created)
        {
            return ErrorFragment();
        }

        await LoadAsync(cancellationToken);

        return Partial("_Tournaments", this);
    }

    /// <summary>
    /// Führt eine Platzänderung aus und liefert die Plätze neu.
    ///
    /// Immer der ganze Abschnitt und nicht die einzelne Zeile: eine neue
    /// Öffnungszeit kann eine bestehende zusammenfassen, und eine Sperre wirkt
    /// auf die freien Fenster daneben.
    /// </summary>
    private async Task<IActionResult> CourtsAsync(
        Guid clubId,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        ClubId = clubId;

        if (!await TryAsync(action))
        {
            return ErrorFragment();
        }

        await LoadAsync(cancellationToken);

        return Partial("_Courts", this);
    }
}
