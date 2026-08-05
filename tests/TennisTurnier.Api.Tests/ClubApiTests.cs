using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

public sealed class ClubApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private const string SystemAdmin = "system-admin";

    private readonly TennisTurnierApiFactory _factory;

    public ClubApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> SystemAdminClientAsync()
    {
        await _factory.GrantAsync(SystemAdmin, Role.SystemAdmin, ResourceScope.Global);
        return _factory.CreateClientAs(SystemAdmin);
    }

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateClubAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest(name, "Europe/Vienna", "Musterstadt"),
            Json);

        return await CreatedIdAsync(response);
    }

    /// <summary>
    /// Ein Turnier in diesem Verein. Es ist der einzige Weg, den Verein für
    /// jemanden sichtbar zu machen, der nicht Systemadministrator ist: eine
    /// Vereinsrolle gibt es nicht mehr, und die Plätze werden über das Turnier
    /// erreicht, das sie bespielt.
    /// </summary>
    private async Task<Guid> TournamentInAsync(HttpClient admin, Guid clubId)
    {
        var templates = await admin.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);

        return await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft",
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id),
            Json));
    }

    [Fact]
    public async Task Ein_SystemAdmin_kann_einen_Verein_anlegen_und_wieder_lesen()
    {
        var client = await SystemAdminClientAsync();

        var clubId = await CreateClubAsync(client, $"TC Anlegen {Guid.NewGuid():N}");
        var club = await client.GetFromJsonAsync<ClubDetail>($"/api/clubs/{clubId}", Json);

        Assert.NotNull(club);
        Assert.Equal(clubId, club.Id);
        Assert.Equal("Europe/Vienna", club.TimeZoneId);
    }

    [Fact]
    public async Task Ohne_Anmeldung_ist_das_Anlegen_eines_Vereins_nicht_moeglich()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest("TC Anonym", "Europe/Vienna", null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Turnierleiter_darf_keine_Vereine_anlegen()
    {
        var admin = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(admin, $"TC Rechte {Guid.NewGuid():N}");
        var tournamentId = await TournamentInAsync(admin, clubId);

        var subject = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(subject, Role.TournamentDirector, ResourceScope.Tournament(tournamentId));

        var response = await _factory.CreateClientAs(subject).PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest("TC Unerlaubt", "Europe/Vienna", null),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Turnierleiter_sieht_einen_fremden_Verein_nicht()
    {
        // Der Kern von ADR-0004, an der Aussenkante geprüft: 404, nicht 403.
        // Ein 403 würde bestätigen, dass es diesen Verein gibt.
        var admin = await SystemAdminClientAsync();
        var ownClub = await CreateClubAsync(admin, $"TC Eigen {Guid.NewGuid():N}");
        var foreignClub = await CreateClubAsync(admin, $"TC Fremd {Guid.NewGuid():N}");
        var tournamentId = await TournamentInAsync(admin, ownClub);

        var subject = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(subject, Role.TournamentDirector, ResourceScope.Tournament(tournamentId));
        var client = _factory.CreateClientAs(subject);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/clubs/{ownClub}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/clubs/{foreignClub}")).StatusCode);
    }

    [Fact]
    public async Task Die_Liste_zeigt_nur_Vereine_mit_einem_eigenen_Turnier()
    {
        var admin = await SystemAdminClientAsync();
        var ownClub = await CreateClubAsync(admin, $"TC Liste {Guid.NewGuid():N}");
        await CreateClubAsync(admin, $"TC Unsichtbar {Guid.NewGuid():N}");
        var tournamentId = await TournamentInAsync(admin, ownClub);

        var subject = $"director-{Guid.NewGuid():N}";
        await _factory.GrantAsync(subject, Role.TournamentDirector, ResourceScope.Tournament(tournamentId));

        var clubs = await _factory.CreateClientAs(subject)
            .GetFromJsonAsync<List<ClubSummary>>("/api/clubs", Json);

        Assert.NotNull(clubs);
        Assert.Equal([ownClub], clubs.Select(c => c.Id));
    }

    [Fact]
    public async Task Ein_SystemAdmin_kann_Plaetze_und_Oeffnungszeiten_pflegen()
    {
        var client = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(client, $"TC Plätze {Guid.NewGuid():N}");

        var courtId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor, IsCenterCourt: true),
            Json));

        await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts/{courtId}/availability",
            new CreateAvailabilityRequest(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(22, 0),
                new DateOnly(2026, 1, 1),
                null),
            Json));

        var courts = await client.GetFromJsonAsync<List<CourtDetail>>($"/api/clubs/{clubId}/courts", Json);

        var court = Assert.Single(courts!);
        Assert.True(court.IsCenterCourt);
        Assert.Single(court.Availability);
    }

    [Fact]
    public async Task Freie_Fenster_beruecksichtigen_die_Sperren()
    {
        var client = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(client, $"TC Fenster {Guid.NewGuid():N}");

        var courtId = await CreatedIdAsync(await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json));

        await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts/{courtId}/availability",
            new CreateAvailabilityRequest(
                DayOfWeek.Saturday,
                new TimeOnly(8, 0),
                new TimeOnly(22, 0),
                new DateOnly(2026, 1, 1),
                null),
            Json);

        // Punktespiel am Samstagnachmittag: 14:00 bis 18:00 Ortszeit (UTC+2).
        await client.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts/{courtId}/blocks",
            new CreateBlockRequest(
                new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2)),
                BlockReason.LeagueMatch,
                "Herren 45"),
            Json);

        var from = Uri.EscapeDataString("2026-05-16T00:00:00+02:00");
        var to = Uri.EscapeDataString("2026-05-17T00:00:00+02:00");
        var windows = await client.GetFromJsonAsync<List<FreeWindow>>(
            $"/api/clubs/{clubId}/courts/{courtId}/free-windows?from={from}&to={to}",
            Json);

        Assert.NotNull(windows);
        Assert.Equal(2, windows.Count);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2)), windows[0].From);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2)), windows[0].To);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.FromHours(2)), windows[1].From);
        Assert.Equal(new DateTimeOffset(2026, 5, 16, 22, 0, 0, TimeSpan.FromHours(2)), windows[1].To);
    }

    [Fact]
    public async Task Ein_Spieler_sieht_seinen_Verein_aber_nicht_die_internen_Notizen()
    {
        // Wer ein Turnier im Verein hat, sieht ihn — das ergibt sich aus dem
        // Query-Filter. Die Notiz an einer Sperre gehört aber zu ViewInternals,
        // und die hat ein Schiedsrichter nicht. Ohne diese Trennung wäre die
        // Berechtigung bedeutungslos.
        var admin = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(admin, $"TC Notizen {Guid.NewGuid():N}");
        var courtId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json));

        await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts/{courtId}/blocks",
            new CreateBlockRequest(
                new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 16, 18, 0, 0, TimeSpan.Zero),
                BlockReason.Maintenance,
                "Belag defekt, Reklamation läuft"),
            Json);

        var tournamentId = await TournamentInAsync(admin, clubId);
        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));

        var seenByReferee = await _factory.CreateClientAs(referee)
            .GetFromJsonAsync<ClubDetail>($"/api/clubs/{clubId}", Json);
        var seenByAdmin = await admin.GetFromJsonAsync<ClubDetail>($"/api/clubs/{clubId}", Json);

        var blockForReferee = Assert.Single(Assert.Single(seenByReferee!.Courts).Blocks);
        var blockForAdmin = Assert.Single(Assert.Single(seenByAdmin!.Courts).Blocks);

        // Dass der Platz gesperrt ist und warum, darf jeder wissen, der am
        // Turnier beteiligt ist — die interne Notiz dazu nicht.
        Assert.Equal(BlockReason.Maintenance, blockForReferee.Reason);
        Assert.Null(blockForReferee.Note);
        Assert.Equal("Belag defekt, Reklamation läuft", blockForAdmin.Note);
    }

    [Fact]
    public async Task Ein_Platz_darf_nicht_auf_einen_belegten_Namen_umbenannt_werden()
    {
        // Ohne die Prüfung im Aggregat schlüge der eindeutige Index als
        // Datenbankfehler durch — für den Aufrufer ein 500 statt 422.
        var admin = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(admin, $"TC Umbenennen {Guid.NewGuid():N}");

        await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json);
        var secondId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/courts",
            new CreateCourtRequest("Platz 2", CourtSurface.Clay, CourtLocation.Outdoor),
            Json));

        var response = await admin.PutAsJsonAsync(
            $"/api/clubs/{clubId}/courts/{secondId}",
            new UpdateCourtRequest("Platz 1", IsCenterCourt: false, IsActive: true),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Eine_fachlich_ungueltige_Eingabe_wird_zu_422()
    {
        var client = await SystemAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest("TC Zeitzone", "Europe/Atlantis", null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Zwei_gleichnamige_Plaetze_werden_abgewiesen()
    {
        var admin = await SystemAdminClientAsync();
        var clubId = await CreateClubAsync(admin, $"TC Doppelname {Guid.NewGuid():N}");

        var court = new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor);
        await admin.PostAsJsonAsync($"/api/clubs/{clubId}/courts", court, Json);

        var second = await admin.PostAsJsonAsync($"/api/clubs/{clubId}/courts", court, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }
}
