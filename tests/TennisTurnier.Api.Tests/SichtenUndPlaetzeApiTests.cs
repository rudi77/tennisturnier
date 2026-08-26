using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Wer was sieht, und was mit einem Platz geschieht, den es noch gibt, aber
/// nicht mehr geben soll.
///
/// Der Schiedsrichter ist hier der Prüfstein: er darf Ergebnisse eintragen und
/// sonst nichts. Seine Sicht auf dasselbe Turnier ist eine andere als die der
/// Turnierleitung — ohne Kontaktdaten, und ohne die Formatvorlage, die einem
/// anderen gehört. Beides ist keine Feinheit: Kontaktdaten gehören niemandem,
/// der sie nicht braucht (ADR-0003), und eine fremde Vorlage ist für ihn
/// schlicht nicht vorhanden (ADR-0004).
/// </summary>
public sealed class SichtenUndPlaetzeApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SichtenUndPlaetzeApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Ein_Schiedsrichter_sieht_das_Turnier_ohne_Innenansicht()
    {
        var mail = $"schiri-{Guid.NewGuid():N}@example.invalid";
        var schiri = _factory.CreateClientAs($"schiri-{Guid.NewGuid():N}", mail);

        // Erst anmelden, damit es das Konto gibt.
        await schiri.GetAsync("/api/me");

        // Eine eigene Vorlage der Turnierleitung: der Schiedsrichter sieht sie
        // nicht, und das Satzformat des Turniers muss trotzdem herauskommen.
        var leitung = _factory.CreateClientAs($"leitung-{Guid.NewGuid():N}");
        var vorlage = await CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(BuiltInFormats.Knockout with
            {
                Id = $"eigene-{Guid.NewGuid():N}",
                Name = $"Eigene Vorlage {Guid.NewGuid():N}",
            }),
            Json));

        var turnierId = await CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Clubmeisterschaft",
                "TC Test",
                null,
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Singles,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                vorlage),
            Json));

        Assert.Equal(
            HttpStatusCode.Created,
            (await leitung.PostAsJsonAsync(
                $"/api/tournaments/{turnierId}/roles",
                new GrantRoleRequest(mail, Role.Referee),
                Json)).StatusCode);

        // Melden, damit es Kontaktdaten gäbe, die er nicht sehen darf.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await leitung.PostAsync($"/api/tournaments/{turnierId}/registration/open", null)).StatusCode);

        var spieler = await CreatedIdAsync(await leitung.PostAsJsonAsync(
            "/api/players",
            new CreatePlayerRequest("Anna", "Berger", "anna@example.invalid", "+43 1 234", null),
            Json));

        var teilnehmer = await leitung.PostAsJsonAsync(
            "/api/participants", new CreateParticipantRequest(spieler, null, null), Json);

        var teilnehmerId = (await teilnehmer.Content.ReadFromJsonAsync<ParticipantSummary>(Json))!.Id;

        await leitung.PostAsJsonAsync(
            $"/api/tournaments/{turnierId}/entries",
            new EnterTournamentRequest(teilnehmerId, null),
            Json);

        var ausLeitungssicht = await leitung.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{turnierId}/entries", Json);

        var derLeitung = Assert.Single(ausLeitungssicht!);

        Assert.Equal("anna@example.invalid", Assert.Single(derLeitung.Contacts).Email);

        // Die Meldungsverwaltung ist die Innenansicht — für ihn verschlossen.
        var verwehrt = await schiri.GetAsync($"/api/tournaments/{turnierId}/entries");
        Assert.Equal(HttpStatusCode.NotFound, verwehrt.StatusCode);

        // Das Satzformat kommt aus der Vorlage, die er nicht sehen darf — als
        // Voreinstellung und nicht als Fehler.
        var turnier = await schiri.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{turnierId}", Json);

        Assert.Null(turnier!.MatchFormat);
        Assert.Equal(new MatchFormat(), turnier.EffectiveMatchFormat);
    }

    [Fact]
    public async Task Ein_stillgelegter_Platz_verschwindet_erst_wenn_nichts_mehr_darauf_steht()
    {
        // Er wird mitten am Turniertag stillgelegt — Regen auf dem Sandplatz.
        // Was schon darauf steht, bleibt sichtbar, sonst verschwände ein
        // laufendes Match aus der Übersicht.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Teilnehmer = 4,
                Plaetze = 2,
                Platzzeiten = true,
                Spielplan = true,
                Turniertag = true,
            });

        var belegt = (await turnier.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json))!
            .First(c => c.Queue.Count > 0);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/tournaments/{turnier.TournamentId}/courts/{belegt.CourtId}",
                new UpdateCourtRequest(belegt.CourtName, IsCenterCourt: false, IsActive: false),
                Json)).StatusCode);

        var danach = await turnier.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json);

        Assert.Contains(danach!, c => c.CourtId == belegt.CourtId);
    }

    [Fact]
    public async Task Ein_festgenagelter_Platz_bleibt_festgenagelt()
    {
        // „Pinned" heißt: der Solver rührt die Ansetzung nicht an. Ohne diese
        // Unterscheidung wäre der Schalter in der Oberfläche wirkungslos.
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true });

        var phasen = await turnier.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{turnier.TournamentId}/phases", Json);

        var match = phasen!.SelectMany(p => p.Matches).First(m => m.Status == MatchStatus.Ready);
        var beginn = new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.FromHours(2));

        var zuweisung = await turnier.Admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(
                turnier.CourtIds[0], 1, beginn, null, TimeSpan.FromMinutes(60), Pinned: true),
            Json);

        Assert.Equal(HttpStatusCode.OK, zuweisung.StatusCode);

        var vorschlag = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/schedule/proposal", null);

        var plan = await vorschlag.Content.ReadFromJsonAsync<SchedulePlanResult>(Json);
        var festgenagelt = plan!.Assignments.Single(a => a.MatchId == match.Id);

        Assert.Equal(beginn, festgenagelt.PlannedStart);
        Assert.Contains("Festgenagelt", festgenagelt.Reason, StringComparison.Ordinal);
    }
}
