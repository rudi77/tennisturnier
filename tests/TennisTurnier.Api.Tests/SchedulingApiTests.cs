using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Spielplan über die API — die Abnahmebedingung für M6.
///
/// Zwei Schritte, und das ist der Kern: rechnen ändert nichts, erst das
/// Bestätigen wirkt. Ein Solverlauf, der den Plan still überschreibt, ist genau
/// das, was Turnierleitungen dazu bringt, die Automatik abzuschalten (ADR-0002).
/// </summary>
public sealed class SchedulingApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SchedulingApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Admin, Guid TournamentId)> DrawnAsync(
        int participants = 16,
        int courts = 4)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "plan-admin",
            new TurnierWunsch
            {
                Anlage = "TC Spielplan",
                Teilnehmer = participants,
                Plaetze = courts,
                CenterCourt = true,
                Platzzeiten = true,
                Oeffentlich = true,
            });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    private static async Task<SchedulePlanResult> ProposeAsync(HttpClient client, Guid tournamentId)
    {
        var response = await client.PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;
    }

    private static ConfirmScheduleRequest From(SchedulePlanResult proposal) =>
        new([.. proposal.Assignments.Select(assignment => new ConfirmedAssignment(
            assignment.MatchId,
            assignment.CourtId,
            assignment.SequenceOnCourt,
            assignment.PlannedStart,
            assignment.EstimatedDuration))]);

    private static async Task<List<PhaseDetail>> PhasesAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;

    [Fact]
    public async Task Ein_Vorschlag_setzt_jedes_Match_an_und_begruendet_es()
    {
        var (admin, tournamentId) = await DrawnAsync();

        var proposal = await ProposeAsync(admin, tournamentId);
        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.Equal(matches.Count, proposal.Assignments.Count);
        Assert.Empty(proposal.Unscheduled);
        Assert.Empty(proposal.Violations);
        Assert.All(proposal.Assignments, a => Assert.False(string.IsNullOrWhiteSpace(a.Reason)));
        Assert.Equal(matches.Count, proposal.Diff.Added);
    }

    [Fact]
    public async Task Ein_Vorschlag_allein_veraendert_nichts()
    {
        // Der Kern der Trennung: Rechnen ist folgenlos.
        var (admin, tournamentId) = await DrawnAsync();

        await ProposeAsync(admin, tournamentId);

        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.All(matches, match => Assert.Null(match.Assignment));
    }

    [Fact]
    public async Task Erst_die_Bestaetigung_traegt_den_Plan_ein()
    {
        var (admin, tournamentId) = await DrawnAsync();
        var proposal = await ProposeAsync(admin, tournamentId);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;

        Assert.All(matches, match => Assert.NotNull(match.Assignment));
        Assert.All(matches, match => Assert.Equal(AssignmentSource.Auto, match.Assignment!.Source));
    }

    [Fact]
    public async Task Ein_bestaetigter_Plan_steht_auch_in_der_oeffentlichen_Ansicht()
    {
        var (admin, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var queued = view.GetProperty("courts").EnumerateArray()
            .Sum(court => court.GetProperty("queue").GetArrayLength());

        Assert.Equal((await PhasesAsync(admin, tournamentId)).Single().Matches.Count, queued);
    }

    [Fact]
    public async Task Ein_zweiter_Lauf_nach_der_Bestaetigung_schlaegt_nichts_Neues_vor()
    {
        // Ohne diese Eigenschaft bekäme die Turnierleitung bei jedem Klick einen
        // anderen Aushang.
        var (admin, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var again = await ProposeAsync(admin, tournamentId);

        Assert.Equal(0, again.Diff.Moved);
        Assert.Equal(0, again.Diff.Added);
        Assert.All(again.Assignments, a => Assert.Equal(ProposalChange.Unchanged, a.Change));
    }

    [Fact]
    public async Task Eine_Verschiebung_von_Hand_ueberlebt_den_naechsten_Lauf()
    {
        var (admin, tournamentId) = await DrawnAsync();
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var match = (await PhasesAsync(admin, tournamentId)).Single().Matches.First(m => m.Round == 1);
        var courts = await admin.PlaetzeAsync(tournamentId);
        var moved = new DateTimeOffset(2026, 5, 17, 15, 0, 0, TimeSpan.FromHours(2));

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(courts![^1].Id, 1, moved, null, TimeSpan.FromMinutes(75)),
            Json);

        var again = await ProposeAsync(admin, tournamentId);
        var kept = again.Assignments.Single(a => a.MatchId == match.Id);

        Assert.Equal(moved, kept.PlannedStart);
        Assert.Equal(courts[^1].Id, kept.CourtId);
        Assert.Equal(ProposalChange.Unchanged, kept.Change);
        Assert.Contains("Hand", kept.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_Platz_wird_der_Vorschlag_ausdruecklich_abgewiesen()
    {
        // Bislang kam hier ein leerer Vorschlag zurück, in dem jedes Match als
        // „nicht angesetzt" stand. Solange die Plätze am Verein hingen, war das
        // der seltene Fall eines Vereins ohne Öffnungszeiten. Seit sie am Turnier
        // hängen, ist es der Normalfall eines frisch angelegten Turniers — und
        // der Veranstalter säße vor einem leeren Board, ohne dass irgendwo
        // stünde, warum.
        var (admin, tournamentId) = await DrawnAsync(participants: 4, courts: 0);

        var response = await admin.PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "Platzzeit",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_Platz_ohne_Zeiten_zaehlt_wie_kein_Platz()
    {
        // Der andere Weg zum selben Ergebnis: es gibt einen Platz, aber er hat
        // kein Fenster. Der Test davor trifft den Fall „gar kein Platz" und
        // nicht diesen — ein angelegter Platz ohne Zeiten sieht in der
        // Oberfläche nach Fortschritt aus und ist doch keiner.
        var (admin, tournamentId) = await DrawnAsync(participants: 4, courts: 0);

        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts",
            new CreateCourtRequest("Platz ohne Zeiten", CourtSurface.Clay, CourtLocation.Outdoor),
            Json);

        var response = await admin.PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Auslosen_geht_auch_ohne_gebuchte_Plaetze()
    {
        // Ausdrücklich erlaubt: Meldeschluss und Platzbuchung sind zwei
        // Vorgänge, und wer zuerst auslost und dann beim Verein anruft, geht
        // keinen falschen Weg. Abgewiesen wird erst der Spielplan — dort, wo die
        // leere Antwort sonst unerklärlich wäre.
        var aufbau = await _factory.NeuesTurnierAsync(
            "plan-ohne-platz",
            new TurnierWunsch { Anlage = "TC Ohne Platz", Teilnehmer = 4, Plaetze = 0 });

        var detail = await aufbau.Admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{aufbau.TournamentId}", Json);

        Assert.Equal(TournamentState.DrawGenerated, detail!.State);
    }

    // --- Was eine Bestätigung nicht darf -----------------------------------

    [Fact]
    public async Task Dasselbe_Match_zweimal_zu_bestaetigen_wird_abgewiesen()
    {
        // Regression: der Anlegepfad prüfte nichts. Ein Body mit demselben Match
        // zweimal legte zwei Zuweisungen an — das Match stand danach zur selben
        // Zeit auf zwei Plätzen.
        var (admin, tournamentId) = await DrawnAsync(participants: 4);
        var proposal = await ProposeAsync(admin, tournamentId);

        var first = proposal.Assignments[0];
        var request = new ConfirmScheduleRequest(
        [
            new ConfirmedAssignment(
                first.MatchId, first.CourtId, 1, first.PlannedStart, first.EstimatedDuration),
            new ConfirmedAssignment(
                first.MatchId, first.CourtId, 2, first.PlannedStart.AddHours(3), first.EstimatedDuration),
        ]);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", request, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var matches = (await PhasesAsync(admin, tournamentId)).Single().Matches;
        Assert.All(matches, match => Assert.Null(match.Assignment));
    }

    [Fact]
    public async Task Zwei_gleichzeitige_Bestaetigungen_legen_den_Plan_nicht_doppelt_an()
    {
        // Regression: beim ersten Bestätigen entstehen lauter neue Zeilen, und auf
        // einer noch nicht existierenden Zeile wirkt kein Zähler. Beide Requests
        // liefen durch und hinterließen jedes Match doppelt in der Platzbelegung —
        // sichtbar nur in der öffentlichen Warteschlange.
        var (admin, tournamentId) = await DrawnAsync(participants: 8);
        var request = From(await ProposeAsync(admin, tournamentId));

        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/schedule/confirm", request, Json),
            admin.PostAsJsonAsync($"/api/tournaments/{tournamentId}/schedule/confirm", request, Json));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var queued = view.GetProperty("courts").EnumerateArray()
            .SelectMany(court => court.GetProperty("queue").EnumerateArray())
            .Select(slot => slot.GetProperty("matchId").GetGuid())
            .ToList();

        Assert.Equal(queued.Count, queued.Distinct().Count());
    }

    [Fact]
    public async Task Eine_Kollision_mit_dem_gespeicherten_Plan_wird_gemeldet()
    {
        // Regression: geprüft wurde nur, was in der Bestätigung stand. Eine
        // Ansetzung, die genau auf einer bereits gespeicherten lag, ging als
        // verstoßfrei durch — der Prüfer sah die andere Hälfte nie.
        var (admin, tournamentId) = await DrawnAsync(participants: 8);
        var proposal = await ProposeAsync(admin, tournamentId);

        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json);

        var target = proposal.Assignments[0];
        var other = proposal.Assignments.First(a =>
            a.MatchId != target.MatchId && a.PlannedStart != target.PlannedStart);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest(
            [
                new ConfirmedAssignment(
                    other.MatchId, target.CourtId, 1, target.PlannedStart, target.EstimatedDuration),
            ]),
            Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = (await response.Content.ReadFromJsonAsync<SchedulePlanResult>(Json))!;
        Assert.Contains(result.Violations, v => v.Constraint == ScheduleConstraint.CourtDoubleBooked);
    }

    [Fact]
    public async Task Die_Ansetzung_eines_gespielten_Matches_wird_abgeraeumt()
    {
        // Regression: der Diff meldete sie als entfernt, entfernt wurde sie nie.
        // Sie blieb als Platzhalter im Spielplan stehen — in der öffentlichen
        // Warteschlange sichtbar und als stiller Kollisionspartner für alles,
        // was danach dorthin geplant wird.
        //
        // Seit M7 gibt die Ergebniseingabe den Platz sofort frei; hier bleibt zu
        // prüfen, dass danach nichts mehr übrig ist, das ein Spielplanlauf
        // abräumen müsste.
        var (admin, tournamentId) = await DrawnAsync(participants: 8);
        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            From(await ProposeAsync(admin, tournamentId)),
            Json);

        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var again = await ProposeAsync(admin, tournamentId);
        Assert.Equal(0, again.Diff.Removed);

        await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(again), Json);

        var view = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var queued = view.GetProperty("courts").EnumerateArray()
            .SelectMany(court => court.GetProperty("queue").EnumerateArray())
            .Select(slot => slot.GetProperty("matchId").GetGuid())
            .ToList();

        Assert.DoesNotContain(match.Id, queued);
    }

    [Fact]
    public async Task Ein_ueberholter_Vorschlag_endet_als_Konflikt()
    {
        // Regression: das gespielte Match fehlte in der Aufgabe, die Bestätigung
        // brach mit 404 ab. Für den Aufrufer heißt das „such weiter" — richtig
        // ist „rechne neu".
        var (admin, tournamentId) = await DrawnAsync(participants: 8);
        var proposal = await ProposeAsync(admin, tournamentId);

        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Ein_Beginn_ausserhalb_des_Turnierzeitraums_wird_abgewiesen()
    {
        var (admin, tournamentId) = await DrawnAsync(participants: 4);
        var proposal = await ProposeAsync(admin, tournamentId);
        var first = proposal.Assignments[0];

        var response = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new ConfirmScheduleRequest(
            [
                new ConfirmedAssignment(
                    first.MatchId,
                    first.CourtId,
                    1,
                    new DateTimeOffset(2099, 1, 1, 10, 0, 0, TimeSpan.Zero),
                    first.EstimatedDuration),
            ]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Bestaetigung_ohne_Ansetzungen_ist_ein_Fehler_der_Anfrage()
    {
        var (admin, tournamentId) = await DrawnAsync(participants: 4);

        var response = await admin.PostAsync(
            $"/api/tournaments/{tournamentId}/schedule/confirm",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Eine_Hand_Ansetzung_auf_einem_stillgelegten_Platz_sagt_was_zu_tun_ist()
    {
        // Regression: der Solver reichte sie weiter, die Bestätigung fand den
        // Platz nicht und antwortete 404 — der ganze Spielplan war blockiert,
        // ohne dass irgendwo stand, warum.
        var (admin, tournamentId) = await DrawnAsync(participants: 4);
        var match = (await PhasesAsync(admin, tournamentId)).Single().Matches[0];

        var courts = await admin.PlaetzeAsync(tournamentId);
        var court = courts![^1];

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(
                court.Id, 1, new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.FromHours(2)), null, null),
            Json);

        // Derselbe Name: eine Umbenennung auf einen schon vergebenen wäre eine
        // Absage, und der Platz bliebe aktiv — der Test prüfte dann nichts.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts/{court.Id}",
                new UpdateCourtRequest(court.Name, IsCenterCourt: false, IsActive: false),
                Json)).StatusCode);

        var proposal = await ProposeAsync(admin, tournamentId);

        Assert.Contains(proposal.Unscheduled, un => un.MatchId == match.Id);
        Assert.Contains(
            "stillgelegt",
            proposal.Unscheduled.Single(un => un.MatchId == match.Id).Reason,
            StringComparison.Ordinal);

        // Und der Rest des Plans lässt sich bestätigen.
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json)).StatusCode);
    }

    [Fact]
    public async Task Im_Turniertagbetrieb_wird_nicht_mehr_geplant()
    {
        // Dort ist eine Startzeit eine Behauptung; es zählt die Reihenfolge auf
        // dem Platz (ADR-0002). Das gilt für beide Schritte: auch ein Vorschlag
        // wäre dort eine Liste von Uhrzeiten, die niemand zusagen kann.
        var (admin, tournamentId) = await DrawnAsync();
        var proposal = await ProposeAsync(admin, tournamentId);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/schedule/confirm", From(proposal), Json)).StatusCode);
    }

    [Fact]
    public async Task Ein_Schiedsrichter_darf_keinen_Plan_rechnen_lassen()
    {
        var (_, tournamentId) = await DrawnAsync(participants: 4);

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(tournamentId));

        var response = await _factory.CreateClientAs(referee)
            .PostAsync($"/api/tournaments/{tournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_bereits_gespieltes_Match_wird_nicht_mehr_eingeplant()
    {
        // Zeit für etwas zu reservieren, das schon gespielt ist, wäre ein Platz,
        // der den ganzen Tag leer bleibt.
        var (admin, tournamentId) = await DrawnAsync();
        var match = (await PhasesAsync(admin, tournamentId)).Single()
            .Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var proposal = await ProposeAsync(admin, tournamentId);

        Assert.DoesNotContain(proposal.Assignments, a => a.MatchId == match.Id);
        Assert.DoesNotContain(proposal.Unscheduled, u => u.MatchId == match.Id);
    }
}
