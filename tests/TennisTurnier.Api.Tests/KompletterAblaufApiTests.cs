using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Clubs;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der ganze Weg am Stück: leere Datenbank → Verein → Plätze → Öffnungszeiten →
/// Turnier → Meldungen → Draw → Spielplan → Turniertag → Ergebnis.
///
/// Die übrigen Testklassen prüfen je einen Abschnitt und bauen sich den Rest als
/// Vorbedingung zusammen. Genau das verdeckt aber die Sorte Fehler, die hier
/// gesucht wird: eine Strecke, die für sich stimmt, aber nirgends beginnt. Ein
/// frisch angelegtes Turnier ohne Weg zum Draw war ein solcher Fall — jeder
/// Abschnitt funktionierte, der Übergang fehlte.
///
/// Die Aufrufe folgen deshalb der Reihenfolge, in der die Oberfläche sie macht:
/// ClubScreen, WizardScreen, DrawPreparation, BoardScreen.
/// </summary>
public sealed class KompletterAblaufApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public KompletterAblaufApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private static void AssertOk(HttpResponseMessage response, string step) =>
        Assert.True(
            response.IsSuccessStatusCode,
            $"{step} scheiterte mit {(int)response.StatusCode} {response.StatusCode}.");

    [Fact]
    public async Task Vom_leeren_System_bis_zum_ersten_Ergebnis()
    {
        await _factory.GrantAsync("ablauf-admin", Role.SystemAdmin, ResourceScope.Global);
        var admin = _factory.CreateClientAs("ablauf-admin");

        // --- ClubScreen: Verein ------------------------------------------------
        var clubId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TS Maria Alm {Guid.NewGuid():N}", "Europe/Vienna", "Maria Alm"),
            Json));

        // --- ClubScreen: Plätze und Öffnungszeiten -----------------------------
        // Zwei Plätze, jeden Wochentag von 8 bis 22 Uhr — dieselbe Massenanlage,
        // die der Screen macht. Ohne Zeitfenster hätte der Solver nichts.
        var courtIds = new List<Guid>();
        for (var i = 1; i <= 2; i++)
        {
            var courtId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/clubs/{clubId}/courts",
                new CreateCourtRequest($"Platz {i}", CourtSurface.Clay, CourtLocation.Outdoor, IsCenterCourt: i == 1),
                Json));

            courtIds.Add(courtId);

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                AssertOk(
                    await admin.PostAsJsonAsync(
                        $"/api/clubs/{clubId}/courts/{courtId}/availability",
                        new CreateAvailabilityRequest(
                            day, new TimeOnly(8, 0), new TimeOnly(22, 0), new DateOnly(2026, 1, 1), null),
                        Json),
                    $"Öffnungszeit {day} auf Platz {i}");
            }
        }

        var club = await admin.GetFromJsonAsync<ClubDetail>($"/api/clubs/{clubId}", Json);
        Assert.Equal(2, club!.Courts.Count);
        Assert.All(club.Courts, court => Assert.Equal(7, court.Availability.Count));

        // --- WizardScreen: Turnier --------------------------------------------
        var templates = await admin.GetFromJsonAsync<List<FormatTemplateSummary>>(
            $"/api/clubs/{clubId}/format-templates", Json);

        var tournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            $"/api/clubs/{clubId}/tournaments",
            new CreateTournamentRequest(
                "Doppel-Clubmeisterschaft 2026",
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id),
            Json));

        var frisch = await admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.Draft, frisch!.State);

        // Der Turniertag ist hier zu Recht verschlossen — das war der Punkt, an
        // dem die Oberfläche vorher endete, ohne einen Weg weiter zu zeigen.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null)).StatusCode);

        // --- DrawPreparation: Meldung öffnen ----------------------------------
        AssertOk(
            await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null),
            "Meldung öffnen");

        // --- DrawPreparation: vier Doppel melden ------------------------------
        foreach (var team in new[] { "Die Netzroller", "Grundlinie Süd", "Volleyfreunde", "Rückhand Royal" })
        {
            var first = await CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest("Anna", $"A{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var second = await CreatedIdAsync(await admin.PostAsJsonAsync(
                "/api/players",
                new CreatePlayerRequest("Eva", $"B{Guid.NewGuid():N}"[..10], null, null, null),
                Json));

            var participant = await (await admin.PostAsJsonAsync(
                "/api/participants", new CreateParticipantRequest(first, second, team), Json))
                .Content.ReadFromJsonAsync<ParticipantSummary>(Json);

            Assert.StartsWith(team, participant!.DisplayName, StringComparison.Ordinal);
            Assert.Equal(2, participant.PlayerIds.Count);

            var entryId = await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/entries",
                new EnterTournamentRequest(participant.Id, null),
                Json));

            AssertOk(
                await admin.PostAsync($"/api/tournaments/{tournamentId}/entries/{entryId}/accept", null),
                $"Meldung annehmen ({team})");
        }

        // --- DrawPreparation: Meldeschluss und Auslosung ----------------------
        AssertOk(
            await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/close", null),
            "Meldung schließen");

        AssertOk(await admin.PostAsync($"/api/tournaments/{tournamentId}/draw", null), "Auslosen");

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);

        // Vier Teams im K.-o.-System: zwei Halbfinale, das Finale und das Spiel
        // um Platz 3 — die eingebaute Vorlage führt ThirdPlaceMatch.
        Assert.Equal(4, phases!.Single().Matches.Count);

        // Die Herkunft steht im Klartext, nicht als Kennung. Sie stand hier
        // einmal als rohe Guid — die öffentliche Projektion löste sie auf, die
        // Ansicht der Turnierleitung nicht, obwohl der Vertrag beides zusagt.
        var hauptfeld = phases!.Single();
        var finale = hauptfeld.Matches.Single(match => match.Label == "Finale");

        Assert.Equal("Sieger aus Halbfinale 1", finale.Side1.Origin);
        Assert.Equal("Sieger aus Halbfinale 2", finale.Side2.Origin);
        Assert.All(
            hauptfeld.Matches,
            match => Assert.All(
                new[] { match.Side1.Origin, match.Side2.Origin },
                origin => Assert.False(
                    Guid.TryParse(origin.Split(' ').Last(), out _),
                    $"Die Herkunft „{origin}“ endet auf einer Kennung statt auf einem Namen.")));

        // --- BoardScreen: Spielplan rechnen und übernehmen --------------------
        var proposalResponse = await admin.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/proposal", null);
        AssertOk(proposalResponse, "Vorschlag");

        var proposal = await proposalResponse.Content.ReadFromJsonAsync<SchedulePlanResult>(Json);
        Assert.NotEmpty(proposal!.Assignments);
        Assert.Empty(proposal.Unscheduled);

        // Rechnen allein ändert nichts (ADR-0002) — erst das Bestätigen wirkt.
        var vorBestaetigung = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        Assert.All(vorBestaetigung!.Single().Matches, match => Assert.Null(match.Assignment));

        AssertOk(
            await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/schedule/confirm",
                new ConfirmScheduleRequest(
                    [.. proposal.Assignments.Select(a => new ConfirmedAssignment(
                        a.MatchId, a.CourtId, a.SequenceOnCourt, a.PlannedStart, a.EstimatedDuration))]),
                Json),
            "Spielplan bestätigen");

        // --- BoardScreen: Turniertag ------------------------------------------
        AssertOk(
            await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null),
            "Turniertag einschalten");

        var amTurniertag = await admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(SchedulingMode.MatchDay, amTurniertag!.SchedulingMode);

        // --- Turniertag: ein Match aufrufen, starten, beenden -----------------
        var spielbar = (await admin.GetFromJsonAsync<List<PhaseDetail>>(
                $"/api/tournaments/{tournamentId}/phases", Json))!
            .Single().Matches
            .First(match => match.Status == MatchStatus.Ready);

        // Aufrufen und Starten hängen an der Zuweisung, nicht am Match: was am
        // Turniertag abläuft, ist „dieses Match auf diesem Platz zu dieser
        // Zeit" — ohne Platz gibt es nichts aufzurufen.
        var zuweisung = spielbar.Assignment;
        Assert.NotNull(zuweisung);

        AssertOk(await admin.PostAsync($"/api/assignments/{zuweisung.Id}/call", null), "Match aufrufen");
        AssertOk(await admin.PostAsync($"/api/assignments/{zuweisung.Id}/start", null), "Match starten");

        AssertOk(
            await admin.PutAsJsonAsync(
                $"/api/matches/{spielbar.Id}/result",
                new RecordResultRequest(
                    MatchOutcome.Normal,
                    [new SetScore(6, 3), new SetScore(6, 4)],
                    null,
                    null),
                Json),
            "Ergebnis eintragen");

        // --- Die öffentliche Ansicht trägt das Ergebnis nach außen ------------
        var oeffentlich = await admin.GetAsync($"/public/tournaments/{tournamentId}");
        AssertOk(oeffentlich, "Öffentliche Ansicht");

        var sicht = await oeffentlich.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("InProgress", sicht.GetProperty("state").GetString());
    }
}
