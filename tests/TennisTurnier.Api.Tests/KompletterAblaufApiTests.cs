using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Registration;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Scheduling;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der ganze Weg am Stück: leere Datenbank → Anmeldung → Turnier samt Ort und
/// Disziplin → Plätze → Platzzeiten → Meldungen → Draw → Spielplan → Turniertag
/// → Ergebnis.
///
/// Die übrigen Testklassen prüfen je einen Abschnitt und bauen sich den Rest als
/// Vorbedingung zusammen. Genau das verdeckt aber die Sorte Fehler, die hier
/// gesucht wird: eine Strecke, die für sich stimmt, aber nirgends beginnt. Ein
/// frisch angelegtes Turnier ohne Weg zum Draw war ein solcher Fall — jeder
/// Abschnitt funktionierte, der Übergang fehlte.
///
/// Der Anfang war einmal ein Verein, den jemand anlegen musste, bevor
/// irgendetwas ging. Er ist ersatzlos entfallen: wer sich anmeldet, darf
/// ausschreiben, und der erste Aufruf ist das Turnier selbst.
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
        // --- Anmeldung: kein Eintrag in einer Konfiguration, keine Freischaltung
        var admin = _factory.CreateClientAs($"ablauf-{Guid.NewGuid():N}");

        var me = await admin.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.False(me!.IsSystemAdmin);
        Assert.Contains(me.Roles, r => r.Role == Role.Organizer && r.Scope == ScopeType.Global);

        // --- WizardScreen, Schritt 1: Eckdaten --------------------------------
        // Ort, Zeitraum und Disziplin stehen am Turnier. Alle drei stehen im
        // Anlegen und nicht in einer späteren Einstellung: ohne Zeitzone ist
        // keine Platzzeit auf die Zeitachse abzubilden, und ohne Disziplin
        // entschiede der erste Melder, was für ein Turnier es wird.
        var templates = await admin.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        var tournamentId = await CreatedIdAsync(await admin.PostAsJsonAsync(
            "/api/tournaments",
            new CreateTournamentRequest(
                "Doppel-Clubmeisterschaft 2026",
                "TC Maria Alm",
                "Am Gemeindeberg 1",
                "Maria Alm",
                "Europe/Vienna",
                Discipline.Doubles,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 17),
                templates!.Single(t => t.Name == BuiltInFormats.Knockout.Name).Id),
            Json));

        // Der Anleger führt sein Turnier — in derselben Arbeitseinheit vergeben.
        // Ohne diese Zuweisung wäre es für ihn im nächsten Augenblick nicht mehr
        // auffindbar, und ohne Rolle gäbe es keinen Weg zurück.
        var nachAnlage = await admin.GetFromJsonAsync<MeResponse>("/api/me", Json);
        Assert.Contains(
            nachAnlage!.Roles,
            r => r.Role == Role.TournamentDirector && r.ResourceId == tournamentId);

        var meine = await admin.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        var eintrag = Assert.Single(meine!);
        Assert.Equal(tournamentId, eintrag.Id);
        Assert.Equal("TC Maria Alm", eintrag.VenueName);

        var frisch = await admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(TournamentState.Draft, frisch!.State);
        Assert.Equal("Europe/Vienna", frisch.Venue.TimeZoneId);
        Assert.Equal(Discipline.Doubles, frisch.Discipline);

        // Der Turniertag ist hier zu Recht verschlossen — das war der Punkt, an
        // dem die Oberfläche vorher endete, ohne einen Weg weiter zu zeigen.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/match-day", null)).StatusCode);

        // --- WizardScreen, Schritt „Plätze": zwei Plätze und ihre Zeiten ------
        var courtIds = new List<Guid>();
        for (var i = 1; i <= 2; i++)
        {
            courtIds.Add(await CreatedIdAsync(await admin.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/courts",
                new CreateCourtRequest(
                    $"Platz {i}", CourtSurface.Clay, CourtLocation.Outdoor, IsCenterCourt: i == 1),
                Json)));
        }

        // Die Massenanlage: beide Plätze, beide Turniertage, acht bis
        // zweiundzwanzig Uhr Ortszeit. Genau das, was am Telefon vereinbart
        // wurde — und ein Aufruf statt vierzehn.
        var windows = await admin.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/courts/windows",
            new CreateCourtWindowsRequest(new TimeOnly(8, 0), new TimeOnly(22, 0)),
            Json);
        AssertOk(windows, "Platzzeiten anlegen");

        Assert.Equal(
            4,
            (await windows.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("created").GetInt32());

        var mitPlaetzen = await admin.GetFromJsonAsync<TournamentDetail>(
            $"/api/tournaments/{tournamentId}", Json);
        Assert.Equal(2, mitPlaetzen!.Courts.Count);
        Assert.All(mitPlaetzen.Courts, court => Assert.Equal(2, court.Windows.Count));

        // --- EntriesScreen: Meldung öffnen und den Link holen -----------------
        AssertOk(
            await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/open", null),
            "Meldung öffnen");

        var link = await admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{tournamentId}/registration", Json);

        Assert.False(string.IsNullOrWhiteSpace(link!.Token));

        // --- Vier Doppel melden sich selbst, ohne Konto -----------------------
        // Jedes über einen eigenen anonymen Client — so, wie vier Menschen auf
        // vier Telefonen denselben Aushang abfotografieren.
        var teams = new[] { "Die Netzroller", "Grundlinie Süd", "Volleyfreunde", "Rückhand Royal" };
        var codes = new List<string>();

        foreach (var team in teams)
        {
            var melder = _factory.CreateClient();
            var meldung = new SelfRegistrationRequest(
                "Anna",
                $"A{Guid.NewGuid():N}"[..10],
                $"anna.{Guid.NewGuid():N}"[..14] + "@example.invalid",
                "+43 1 2345678",
                "Eva",
                $"B{Guid.NewGuid():N}"[..10],
                $"eva.{Guid.NewGuid():N}"[..13] + "@example.invalid",
                team);

            var response = await melder.PostAsJsonAsync(
                $"/public/registrations/{link.Token}", meldung, Json);
            AssertOk(response, $"Selbstmeldung ({team})");

            var result = await response.Content.ReadFromJsonAsync<SelfRegistrationResult>(Json);
            codes.Add(result!.ConfirmationCode);

            // Der erste schickt zweimal — der Doppelklick auf „Absenden" ist der
            // häufigste Fall, und er darf keine zweite Meldung anlegen.
            if (team == teams[0])
            {
                var again = await melder.PostAsJsonAsync(
                    $"/public/registrations/{link.Token}", meldung, Json);
                AssertOk(again, "Zweites Absenden");

                Assert.Equal(
                    result.ConfirmationCode,
                    (await again.Content.ReadFromJsonAsync<SelfRegistrationResult>(Json))!.ConfirmationCode);
            }
        }

        // Ein Einzelner passt hier nicht hinein. Das fiel früher erst auf, wenn
        // überhaupt — die Ausschreibung kannte ihre eigene Disziplin nicht.
        var alleine = await _factory.CreateClient().PostAsJsonAsync(
            $"/public/registrations/{link.Token}",
            new SelfRegistrationRequest(
                "Lisa", $"C{Guid.NewGuid():N}"[..10], "lisa@example.invalid", null, null, null, null, null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, alleine.StatusCode);

        // --- EntriesScreen: vier Meldungen, alle annehmen ---------------------
        var entries = await admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{tournamentId}/entries", Json);

        Assert.Equal(4, entries!.Count);
        Assert.All(entries, entry => Assert.Equal(EntryStatus.Applied, entry.Status));
        Assert.All(entries, entry => Assert.Equal(EntryOrigin.SelfService, entry.Origin));
        Assert.Equal(codes.Distinct().Order(), entries.Select(e => e.ConfirmationCode!).Order());

        foreach (var entry in entries)
        {
            Assert.StartsWith(
                teams.Single(team => entry.ParticipantName.StartsWith(team, StringComparison.Ordinal)),
                entry.ParticipantName,
                StringComparison.Ordinal);

            AssertOk(
                await admin.PostAsync(
                    $"/api/tournaments/{tournamentId}/entries/{entry.Id}/accept", null),
                $"Meldung annehmen ({entry.ParticipantName})");
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

        // Auch die Karten am Platz nennen die Herkunft in Worten. Sie kommen aus
        // einem eigenen Dienst und hatten deshalb ihre eigene Fassung derselben
        // Frage — mit ihrer eigenen Kennung darin.
        var board = await admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{tournamentId}/courts", Json);

        var wartend = board!
            .SelectMany(court => court.Queue.Concat(court.Current is null ? [] : [court.Current]))
            .SelectMany(queued => new[] { queued.Side1, queued.Side2 })
            .Where(name => name is not null)
            .ToList();

        Assert.NotEmpty(wartend);
        Assert.All(
            wartend,
            name => Assert.False(
                Guid.TryParse(name!.Split(' ').Last(), out _),
                $"Auf der Platzkarte steht „{name}“ statt eines Namens."));

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
        Assert.Equal("TC Maria Alm", sicht.GetProperty("venueName").GetString());

        // Was nicht darin steht, ist die eigentliche Aussage: die Adresse nicht
        // — die öffentliche Sicht trägt den Namen der Anlage, weil Zuschauer
        // wissen müssen, wohin sie fahren, und mehr gehört ihr nicht. Der
        // Anmeldetoken nicht, weil er der Schlüssel zum Melden ist. Und keine
        // E-Mail-Adresse, obwohl hier vier Menschen ohne Konto ihre hinterlassen
        // haben (ADR-0003).
        var roh = await oeffentlich.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Am Gemeindeberg", roh, StringComparison.Ordinal);
        Assert.DoesNotContain(link.Token, roh, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", roh, StringComparison.OrdinalIgnoreCase);
        Assert.All(codes, code => Assert.DoesNotContain(code, roh, StringComparison.Ordinal));
    }
}
