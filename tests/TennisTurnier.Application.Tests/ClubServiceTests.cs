using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Common;
using TennisTurnier.Application.Tests.Fakes;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Tests;

public sealed class ClubServiceTests
{
    private readonly MutableUserContext _userContext = new();
    private readonly InMemoryClubRepository _clubs;
    private readonly ClubService _service;

    public ClubServiceTests()
    {
        _clubs = new InMemoryClubRepository(_userContext);
        _service = new ClubService(_clubs, new RecordingUnitOfWork(_clubs), _userContext);
    }

    private static readonly Guid UserId = Guid.NewGuid();

    private void ActAs(params RoleAssignment[] assignments) =>
        _userContext.Current = new UserPrincipal(UserId, assignments);

    private void ActAsSystemAdmin() =>
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.SystemAdmin, ResourceScope.Global));

    private Club SeedClub(string name = "TC Musterstadt")
    {
        var club = new Club(Guid.NewGuid(), name, "Europe/Vienna");
        return _clubs.Seed(club);
    }

    private void ActAsClubAdminOf(Guid clubId) =>
        ActAs(new RoleAssignment(Guid.NewGuid(), UserId, Role.ClubAdmin, ResourceScope.Club(clubId)));

    [Fact]
    public async Task Nur_ein_SystemAdmin_darf_einen_Verein_anlegen()
    {
        ActAs();

        await Assert.ThrowsAsync<AccessDeniedException>(
            () => _service.CreateAsync(new CreateClubRequest("TC Neu", "Europe/Vienna", null)));
    }

    [Fact]
    public async Task Ein_SystemAdmin_legt_einen_Verein_an_und_speichert()
    {
        ActAsSystemAdmin();

        var id = await _service.CreateAsync(new CreateClubRequest("TC Neu", "Europe/Vienna", "Musterstadt"));

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(1, _clubs.SavedChanges);
    }

    [Fact]
    public async Task Ein_Verein_ausserhalb_des_Scopes_wirkt_wie_nicht_vorhanden()
    {
        var foreign = SeedClub("TC Fremd");
        ActAsClubAdminOf(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(foreign.Id));
    }

    [Fact]
    public async Task Die_Liste_enthaelt_nur_Vereine_im_Scope()
    {
        var own = SeedClub("TC Eigen");
        SeedClub("TC Fremd");
        ActAsClubAdminOf(own.Id);

        var clubs = await _service.ListAsync();

        Assert.Equal([own.Id], clubs.Select(c => c.Id));
    }

    [Fact]
    public async Task Ein_ClubAdmin_darf_einen_Platz_anlegen()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);

        var courtId = await _service.AddCourtAsync(
            club.Id,
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor, IsCenterCourt: true));

        var court = Assert.Single(club.Courts);
        Assert.Equal(courtId, court.Id);
        Assert.True(court.IsCenterCourt);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_keine_Plaetze_anlegen()
    {
        // Der Schiedsrichter ist im Verein sichtbar — sehen darf er ihn also,
        // ändern nicht. Genau diese Trennung prüft der Test.
        var club = SeedClub();
        ActAs(
            new RoleAssignment(Guid.NewGuid(), UserId, Role.Player, ResourceScope.Club(club.Id)),
            new RoleAssignment(Guid.NewGuid(), UserId, Role.Referee, ResourceScope.Tournament(Guid.NewGuid())));

        await Assert.ThrowsAsync<AccessDeniedException>(() => _service.AddCourtAsync(
            club.Id,
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor)));
    }

    [Fact]
    public async Task Ein_unbekannter_Platz_liefert_nicht_gefunden()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.AddAvailabilityAsync(
            club.Id,
            Guid.NewGuid(),
            new CreateAvailabilityRequest(
                DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(22, 0), new DateOnly(2026, 1, 1), null)));
    }

    [Fact]
    public async Task Freie_Fenster_ziehen_die_Sperren_ab()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);

        var courtId = await _service.AddCourtAsync(
            club.Id,
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor));

        await _service.AddAvailabilityAsync(club.Id, courtId, new CreateAvailabilityRequest(
            DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(22, 0), new DateOnly(2026, 1, 1), null));

        await _service.AddBlockAsync(club.Id, courtId, new CreateBlockRequest(
            new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2)),
            BlockReason.LeagueMatch,
            null));

        var windows = await _service.GetFreeWindowsAsync(
            club.Id,
            courtId,
            new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(2, windows.Count);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2)), windows[0].From);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)), windows[0].To);
    }

    [Fact]
    public async Task Ein_Platz_laesst_sich_stilllegen_und_reaktivieren()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);
        var courtId = await _service.AddCourtAsync(
            club.Id,
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor));

        await _service.UpdateCourtAsync(club.Id, courtId, new UpdateCourtRequest("Platz 1", false, IsActive: false));
        Assert.False(club.Courts.Single().IsActive);

        await _service.UpdateCourtAsync(club.Id, courtId, new UpdateCourtRequest("Platz 1", false, IsActive: true));
        Assert.True(club.Courts.Single().IsActive);
    }

    [Fact]
    public async Task Jede_aendernde_Handlung_schliesst_mit_einem_Speichern_ab()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);

        var courtId = await _service.AddCourtAsync(
            club.Id,
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor));
        var windowId = await _service.AddAvailabilityAsync(club.Id, courtId, new CreateAvailabilityRequest(
            DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(22, 0), new DateOnly(2026, 1, 1), null));
        await _service.RemoveAvailabilityAsync(club.Id, courtId, windowId);

        Assert.Equal(3, _clubs.SavedChanges);
    }

    [Fact]
    public async Task Lesende_Handlungen_speichern_nicht()
    {
        var club = SeedClub();
        ActAsClubAdminOf(club.Id);

        await _service.GetAsync(club.Id);
        await _service.ListAsync();

        Assert.Equal(0, _clubs.SavedChanges);
    }
}
