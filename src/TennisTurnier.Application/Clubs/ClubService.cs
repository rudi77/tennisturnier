using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.PublicView;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Clubs;

public sealed class ClubService : IClubService
{
    private readonly IClubRepository _clubs;
    private readonly ITournamentRepository _tournaments;
    private readonly IPublicViewService _publicView;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserContext _userContext;

    public ClubService(
        IClubRepository clubs,
        ITournamentRepository tournaments,
        IPublicViewService publicView,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
    {
        _clubs = clubs;
        _tournaments = tournaments;
        _publicView = publicView;
        _unitOfWork = unitOfWork;
        _userContext = userContext;
    }

    private UserPrincipal User => _userContext.Current;

    public async Task<Guid> CreateAsync(CreateClubRequest request, CancellationToken cancellationToken = default)
    {
        User.Require(Permission.ManageClubs, ResourceScope.Global);

        var club = new Club(Guid.NewGuid(), request.Name, request.TimeZoneId, request.City);
        _clubs.Add(club);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return club.Id;
    }

    public async Task<IReadOnlyList<ClubSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clubs = await _clubs.ListAsync(cancellationToken);

        return clubs
            .Select(c => new ClubSummary(c.Id, c.Name, c.City, c.TimeZoneId, c.Courts.Count))
            .ToList();
    }

    public async Task<ClubDetail> GetAsync(Guid clubId, CancellationToken cancellationToken = default)
    {
        var club = await Load(clubId, cancellationToken);

        return new ClubDetail(
            club.Id,
            club.Name,
            club.City,
            club.TimeZoneId,
            club.Courts.Select(court => ToDetail(court, MaySeeInternals(clubId))).ToList());
    }

    public async Task UpdateAsync(
        Guid clubId,
        UpdateClubRequest request,
        CancellationToken cancellationToken = default)
    {
        var club = await Load(clubId, cancellationToken);
        User.Require(Permission.ManageClub, ResourceScope.Club(clubId));

        club.Rename(request.Name);
        club.MoveTo(request.TimeZoneId);
        club.SetCity(request.City);

        await RebuildPublicViewsAsync(clubId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddCourtAsync(
        Guid clubId,
        CreateCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);

        var court = club.AddCourt(Guid.NewGuid(), request.Name, request.Surface, request.Location);
        court.MarkAsCenterCourt(request.IsCenterCourt);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return court.Id;
    }

    public async Task UpdateCourtAsync(
        Guid clubId,
        Guid courtId,
        UpdateCourtRequest request,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);
        var court = CourtOf(club, courtId);

        club.RenameCourt(courtId, request.Name);
        court.MarkAsCenterCourt(request.IsCenterCourt);

        if (request.IsActive)
        {
            court.Reactivate();
        }
        else
        {
            court.Deactivate();
        }

        await RebuildPublicViewsAsync(clubId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Baut die öffentlichen Ansichten aller Turniere des Vereins neu.
    ///
    /// Name des Vereins und Namen der Plätze stehen in der Ansicht (ADR-0003).
    /// Ohne diesen Schritt schickt der Aushang die Zuschauer am Turniertag auf
    /// einen Platz, der inzwischen anders heißt.
    /// </summary>
    private async Task RebuildPublicViewsAsync(Guid clubId, CancellationToken cancellationToken)
    {
        foreach (var tournament in await _tournaments.ListByClubAsync(clubId, cancellationToken))
        {
            await _publicView.RebuildAsync(tournament.Id, cancellationToken);
        }
    }

    public async Task<Guid> AddAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        CreateAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);
        var court = CourtOf(club, courtId);

        var window = court.AddAvailability(
            Guid.NewGuid(),
            request.DayOfWeek,
            request.OpensAt,
            request.ClosesAt,
            request.ValidFrom,
            request.ValidUntil);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return window.Id;
    }

    public async Task RemoveAvailabilityAsync(
        Guid clubId,
        Guid courtId,
        Guid windowId,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);
        CourtOf(club, courtId).RemoveAvailability(windowId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddBlockAsync(
        Guid clubId,
        Guid courtId,
        CreateBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);
        var court = CourtOf(club, courtId);

        var block = court.AddBlock(
            Guid.NewGuid(),
            new TimeSlot(request.From, request.To),
            request.Reason,
            request.Note);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return block.Id;
    }

    public async Task RemoveBlockAsync(
        Guid clubId,
        Guid courtId,
        Guid blockId,
        CancellationToken cancellationToken = default)
    {
        var club = await LoadForCourtManagement(clubId, cancellationToken);
        CourtOf(club, courtId).RemoveBlock(blockId);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FreeWindow>> GetFreeWindowsAsync(
        Guid clubId,
        Guid courtId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var club = await Load(clubId, cancellationToken);
        var court = CourtOf(club, courtId);

        var calendar = new CourtCalendar(club.TimeZone);

        return calendar
            .FreeWindows(court, new TimeSlot(from, to))
            .Select(slot => new FreeWindow(slot.Start, slot.End))
            .ToList();
    }

    /// <summary>
    /// Lädt den Verein oder wirft 404. Der Repository-Filter hat den Scope bereits
    /// angewandt — ein fremder Verein kommt hier als <c>null</c> an und ist damit
    /// von einem nicht existierenden nicht zu unterscheiden.
    /// </summary>
    private async Task<Club> Load(Guid clubId, CancellationToken cancellationToken) =>
        await _clubs.FindAsync(clubId, cancellationToken)
        ?? throw new NotFoundException("Verein", clubId);

    private async Task<Club> LoadForCourtManagement(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await Load(clubId, cancellationToken);
        User.Require(Permission.ManageCourts, ResourceScope.Club(clubId));
        return club;
    }

    /// <summary>
    /// Wer im Verein irgendeine Rolle hat, sieht ihn — auch ein Spieler. Die
    /// internen Notizen an den Sperren gehören aber ausdrücklich zu
    /// <see cref="Permission.ViewInternals"/>. Ohne diese Unterscheidung wäre
    /// die Berechtigung bedeutungslos, weil jedes Vereinsmitglied den Grund
    /// jeder Sperre im Klartext mitlesen könnte.
    /// </summary>
    private bool MaySeeInternals(Guid clubId) =>
        User.Can(Permission.ViewInternals, ResourceScope.Club(clubId));

    private static Court CourtOf(Club club, Guid courtId) =>
        club.Courts.FirstOrDefault(c => c.Id == courtId)
        ?? throw new NotFoundException("Platz", courtId);

    private static CourtDetail ToDetail(Court court, bool includeInternals) => new(
        court.Id,
        court.Name,
        court.Surface,
        court.Location,
        court.IsCenterCourt,
        court.IsActive,
        court.Availability
            .Select(w => new AvailabilityDetail(w.Id, w.DayOfWeek, w.OpensAt, w.ClosesAt, w.ValidFrom, w.ValidUntil))
            .ToList(),
        court.Blocks
            .Select(b => new BlockDetail(
                b.Id,
                b.Period.Start,
                b.Period.End,
                b.Reason,
                includeInternals ? b.Note : null))
            .ToList());
}
