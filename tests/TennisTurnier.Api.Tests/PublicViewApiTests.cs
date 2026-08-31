using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die öffentliche Ansicht (ADR-0003) — ohne Anmeldung, mit ETag, und vor allem
/// ohne alles, was niemanden etwas angeht.
/// </summary>
public sealed class PublicViewApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Feldnamen, die in einer öffentlichen Ansicht nichts verloren haben.
    ///
    /// Diese Liste ist der eigentliche Test: sie schlägt an, wenn jemand dem
    /// Projektionsbauplan ein Feld hinzufügt, ohne über die Folgen nachzudenken.
    /// Ein Blick auf die Ausgabe würde das nicht auffangen — das erste
    /// zusätzliche Feld sieht harmlos aus.
    /// </summary>
    private static readonly string[] ForbiddenFragments =
    [
        "email", "mail", "phone", "telefon", "birth", "geburt", "dateOfBirth",
        "contact", "kontakt", "note", "notiz", "address", "adresse",
        "playerId", "participantId", "entryId", "userId",

        // Seit der Selbstmeldung: das Anmeldetoken ist der Schlüssel zum
        // Melden, der Bestätigungscode der Weg eines Melders zu seiner Meldung.
        // Beide dürfen in einer Antwort, die jeder abrufen kann, nicht
        // vorkommen — auch nicht als Feldname.
        "token", "registration", "anmeldung", "confirmation", "bestaetigung",
    ];

    private readonly TennisTurnierApiFactory _factory;

    public PublicViewApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Ein ausgelostes Turnier, dessen Spieler bewusst vollständige Kontaktdaten
    /// tragen — sonst prüfte die Datensparsamkeit gegen Daten, die es gar nicht
    /// gibt.
    /// </summary>
    private async Task<(HttpClient Admin, Guid TournamentId)> DrawnTournamentAsync(
        int participants = 4)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "public-admin",
            new TurnierWunsch
            {
                Anlage = "TC Öffentlich",
                Teilnehmer = participants,
                Kontaktdaten = true,
                Plaetze = 1,
                // Seit ADR-0012 ist privat die Vorgabe; wer die anonyme
                // Ansicht prüft, öffnet das Turnier ausdrücklich.
                Oeffentlich = true,
            });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    /// <summary>Ein Client ohne jeden Anmeldeheader — der Zuschauer.</summary>
    private HttpClient Spectator() => _factory.CreateClient();

    [Fact]
    public async Task Ein_Zuschauer_ohne_Anmeldung_sieht_das_Bracket()
    {
        var (_, tournamentId) = await DrawnTournamentAsync();

        var response = await Spectator().GetAsync($"/public/tournaments/{tournamentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var view = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var phase = view.GetProperty("phases").EnumerateArray().Single();

        Assert.Equal("Clubmeisterschaft", view.GetProperty("name").GetString());
        Assert.Equal(4, phase.GetProperty("matches").GetArrayLength());
    }

    [Fact]
    public async Task Die_oeffentliche_Ansicht_enthaelt_keine_personenbezogenen_Daten()
    {
        var (_, tournamentId) = await DrawnTournamentAsync();

        var body = await Spectator().GetStringAsync($"/public/tournaments/{tournamentId}");

        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
        }

        // Und die Gegenprobe auf die Werte selbst: ein Feldname ließe sich
        // umbenennen, der Inhalt bliebe derselbe.
        Assert.DoesNotContain("example.invalid", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+43 1 2345678", body, StringComparison.Ordinal);
        Assert.DoesNotContain("1990-03-14", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Der_konkrete_Anmeldetoken_steht_nicht_darin()
    {
        // Die Feldnamen oben ließen sich umbenennen. Dies ist die Gegenprobe auf
        // den Wert: der Token dieses Turniers, wörtlich gesucht.
        var (admin, tournamentId) = await DrawnTournamentAsync();

        var link = await admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{tournamentId}/registration", Json);

        var body = await Spectator().GetStringAsync($"/public/tournaments/{tournamentId}");

        Assert.False(string.IsNullOrWhiteSpace(link!.Token));
        Assert.DoesNotContain(link.Token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Namen_der_Teilnehmer_stehen_darin()
    {
        // Die Gegenprobe zur Datensparsamkeit: ohne sie wäre die Regel oben auch
        // dann erfüllt, wenn die Ansicht schlicht leer bliebe.
        var (admin, tournamentId) = await DrawnTournamentAsync();

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var expected = phases!.Single().Matches
            .SelectMany(m => new[] { m.Side1.ParticipantName, m.Side2.ParticipantName })
            .First(name => name is not null)!;

        var body = await Spectator().GetStringAsync($"/public/tournaments/{tournamentId}");

        Assert.Contains(expected, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Die_Zeitzone_der_Anlage_steht_darin()
    {
        // Ohne sie ist jede Uhrzeit der Antwort mehrdeutig. Ein Zuschauer ohne
        // Anmeldung kann sie nirgends sonst herbekommen — und die Zone seines
        // Geräts ist die falsche Antwort, sobald er angereist ist.
        var (_, tournamentId) = await DrawnTournamentAsync();

        var view = await Spectator().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        Assert.Equal("Europe/Vienna", view.GetProperty("timeZoneId").GetString());
    }

    [Fact]
    public async Task Ein_zweiter_Abruf_mit_dem_ETag_liefert_304()
    {
        var (_, tournamentId) = await DrawnTournamentAsync();
        var spectator = Spectator();

        var first = await spectator.GetAsync($"/public/tournaments/{tournamentId}");
        var etag = first.Headers.ETag;

        Assert.NotNull(etag);
        Assert.True(first.Headers.CacheControl!.NoCache);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/public/tournaments/{tournamentId}");
        request.Headers.IfNoneMatch.Add(etag);

        var second = await spectator.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Ein_neues_Ergebnis_aendert_den_ETag()
    {
        var (admin, tournamentId) = await DrawnTournamentAsync();
        var spectator = Spectator();

        var before = (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).Headers.ETag!;

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.Single().Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var after = await spectator.GetAsync($"/public/tournaments/{tournamentId}");

        Assert.NotEqual(before, after.Headers.ETag);

        var view = await after.Content.ReadFromJsonAsync<JsonElement>(Json);
        var updated = view.GetProperty("phases").EnumerateArray().Single()
            .GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == match.Id);

        Assert.Equal("6:4, 6:2", updated.GetProperty("score").GetString());
        Assert.Equal("Finished", updated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ein_unveraendertes_Turnier_behaelt_seinen_ETag()
    {
        // Der Neuaufbau läuft nach jeder Handlung. Bliebe eine Änderung ohne
        // Wirkung dennoch nicht unbemerkt, wäre jeder Cache wertlos und jeder
        // Zuschauer bekäme fortwährend Pushs ohne Neuigkeit.
        var (admin, tournamentId) = await DrawnTournamentAsync();
        var spectator = Spectator();

        var before = (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).Headers.ETag!;

        await admin.PostAsync($"/api/tournaments/{tournamentId}/scheduling/planning", null);

        var after = (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).Headers.ETag!;

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Ein_Turnier_ohne_Auslosung_hat_keine_oeffentliche_Ansicht()
    {
        // Vor der Auslosung gibt es nur eine Meldeliste, und die ist nicht
        // öffentlich.
        var aufbau = await _factory.NeuesTurnierAsync(
            "public-admin",
            new TurnierWunsch
            {
                Anlage = "TC Entwurf",
                Name = "Noch nicht ausgelost",
                Auslosen = false,
            });

        var response = await Spectator().GetAsync($"/public/tournaments/{aufbau.TournamentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Eine_zurueckgenommene_Auslosung_verschwindet_aus_der_Oeffentlichkeit()
    {
        // Ein stehengebliebenes Bracket zu einem Feld, das es nicht mehr gibt,
        // wäre schlimmer als gar keines.
        var (admin, tournamentId) = await DrawnTournamentAsync();
        var spectator = Spectator();

        Assert.Equal(
            HttpStatusCode.OK,
            (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).StatusCode);

        await admin.PostAsync($"/api/tournaments/{tournamentId}/registration/reopen", null);

        // 204 und nicht 404: das Turnier ist weiterhin offen, es gibt nur
        // gerade nichts zu zeigen. Ein 404 hieße „gibt es nicht oder ist
        // privat" — beides wäre gelogen.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).StatusCode);
    }

    [Fact]
    public async Task Die_Herkunft_einer_offenen_Seite_ist_lesbar()
    {
        // Der Sinn des Summentyps aus ADR-0001 auf der Anzeigeseite: das Bracket
        // erzählt schon etwas, bevor ein Ball gespielt wurde.
        var (_, tournamentId) = await DrawnTournamentAsync();

        var view = await Spectator().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var final = view.GetProperty("phases").EnumerateArray().Single()
            .GetProperty("matches").EnumerateArray()
            .Single(m => m.GetProperty("label").GetString() == "Finale");

        Assert.StartsWith(
            "Sieger aus Halbfinale",
            final.GetProperty("side1").GetProperty("origin").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_zugewiesener_Platz_steht_in_der_Ansicht()
    {
        var (admin, tournamentId) = await DrawnTournamentAsync();

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.Single().Matches.First(m => m.Status == MatchStatus.Ready);

        var courts = await admin.PlaetzeAsync(tournamentId);
        var earliest = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.FromHours(2));

        await admin.PostAsJsonAsync(
            $"/api/matches/{match.Id}/court",
            new AssignCourtRequest(courts!.Single().Id, 1, null, earliest, null),
            Json);

        var view = await Spectator().GetFromJsonAsync<JsonElement>(
            $"/public/tournaments/{tournamentId}", Json);

        var court = view.GetProperty("courts").EnumerateArray().Single();
        var slot = court.GetProperty("queue").EnumerateArray().Single();

        Assert.Equal("Platz 1", court.GetProperty("name").GetString());
        Assert.Equal(match.Id, slot.GetProperty("matchId").GetGuid());
        Assert.Equal(earliest, slot.GetProperty("earliestStart").GetDateTimeOffset());
    }

    [Fact]
    public async Task Die_Turnierleitung_kann_die_Ansicht_neu_aufbauen_lassen()
    {
        // Die Projektion ist abgeleiteter Zustand. Ohne diesen Weg bliebe ein
        // behobener Fehler im Bauplan in allen alten Ständen stehen (ADR-0003).
        var (admin, tournamentId) = await DrawnTournamentAsync();
        var spectator = Spectator();

        var before = (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).Headers.ETag!;

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/tournaments/{tournamentId}/public-view/rebuild", null)).StatusCode);

        var after = (await spectator.GetAsync($"/public/tournaments/{tournamentId}")).Headers.ETag!;

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Ein_Zuschauer_kann_den_Neuaufbau_nicht_ausloesen()
    {
        var (_, tournamentId) = await DrawnTournamentAsync();

        var response = await Spectator()
            .PostAsync($"/api/tournaments/{tournamentId}/public-view/rebuild", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ein_unbekanntes_Turnier_bleibt_unbekannt()
    {
        var response = await Spectator().GetAsync($"/public/tournaments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
