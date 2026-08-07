using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der einfache Ablauf: anlegen, melden, auslosen, starten, spielen, fertig.
///
/// Er kommt ohne Termin und ohne Spielplan aus. Beides gibt es weiterhin — wer
/// Plätze und Zeiten pflegen will, bekommt den vollen Spielplan —, aber nichts
/// davon steht mehr zwischen dem Veranstalter und seinem ersten Turnier.
/// </summary>
public sealed class EinfacherAblaufApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public EinfacherAblaufApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static TurnierWunsch OhneTermin(int teilnehmer, bool auslosen) => new()
    {
        Beginn = null,
        Ende = null,
        Platzzeiten = false,
        Spielplan = false,
        Teilnehmer = teilnehmer,
        Auslosen = auslosen,
    };

    private static async Task<TournamentDetail> TurnierAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{id}", Json))!;

    [Fact]
    public async Task Ein_Turnier_entsteht_ohne_Termin()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-1", OhneTermin(4, auslosen: false));

        var turnier = await TurnierAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Null(turnier.StartsOn);
        Assert.Null(turnier.EndsOn);
        Assert.Equal(TournamentState.RegistrationOpen, turnier.State);
    }

    /// <summary>
    /// Der Termin lässt sich nachtragen — und wieder offenlassen. Ein abgesagter
    /// Termin ist eine gewöhnliche Nachricht und kein Sonderfall.
    /// </summary>
    [Fact]
    public async Task Ein_Termin_lässt_sich_nachtragen_und_wieder_löschen()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-2", OhneTermin(2, auslosen: false));
        var vorher = await TurnierAsync(aufbau.Admin, aufbau.TournamentId);

        async Task SetzeAsync(DateOnly? beginn, DateOnly? ende) => Assert.Equal(
            HttpStatusCode.NoContent,
            (await aufbau.Admin.PutAsJsonAsync(
                $"/api/tournaments/{aufbau.TournamentId}",
                new UpdateTournamentRequest(
                    vorher.Name,
                    vorher.Venue.Name,
                    vorher.Venue.Address,
                    vorher.Venue.City,
                    vorher.Venue.TimeZoneId,
                    vorher.Discipline,
                    beginn,
                    ende),
                Json)).StatusCode);

        await SetzeAsync(new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 17));
        Assert.Equal(new DateOnly(2026, 5, 16), (await TurnierAsync(aufbau.Admin, aufbau.TournamentId)).StartsOn);

        await SetzeAsync(null, null);
        Assert.Null((await TurnierAsync(aufbau.Admin, aufbau.TournamentId)).StartsOn);
    }

    /// <summary>
    /// Ohne Termin gibt es keine Turniertage, auf die eine Zeitspanne fiele. Die
    /// Massenanlage legte vorher wortlos nichts an und meldete Erfolg — jetzt
    /// sagt sie, was fehlt.
    /// </summary>
    [Fact]
    public async Task Ohne_Termin_sagt_die_Platzzeitanlage_was_fehlt()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-3", OhneTermin(2, auslosen: false));

        await aufbau.Admin.PostAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/courts",
            new CreateCourtRequest("Platz 1", CourtSurface.Clay, CourtLocation.Outdoor),
            Json);

        var antwort = await aufbau.Admin.PostAsJsonAsync(
            $"/api/tournaments/{aufbau.TournamentId}/courts/windows",
            new CreateCourtWindowsRequest(new TimeOnly(8, 0), new TimeOnly(20, 0)),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
        Assert.Contains("Termin", await antwort.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Startknopf. Den Endpunkt gab es von Anfang an, aufgerufen hat ihn nie
    /// jemand — der Start folgte allein aus dem ersten Ergebnis. Wer ein
    /// ausgelostes Turnier vor sich hatte, fand keinen Weg weiter.
    /// </summary>
    [Fact]
    public async Task Ein_ausgelostes_Turnier_lässt_sich_ausdruecklich_starten()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-4", OhneTermin(4, auslosen: true));

        Assert.Equal(
            TournamentState.DrawGenerated,
            (await TurnierAsync(aufbau.Admin, aufbau.TournamentId)).State);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/start", null)).StatusCode);

        Assert.Equal(
            TournamentState.InProgress,
            (await TurnierAsync(aufbau.Admin, aufbau.TournamentId)).State);
    }

    /// <summary>
    /// Ohne Termin gibt es keinen Spielplan — und das ist keine Bequemlichkeit,
    /// sondern die Schranke.
    ///
    /// RequireScheduledWithin lässt ohne Termin jeden Zeitpunkt durch; es gibt
    /// keinen Zeitraum, gegen den zu prüfen wäre. Ohne diese Absage weiter
    /// vorn ließe sich ein Spielplan mit „1. Juni 2099“ bestätigen, und er
    /// stünde danach öffentlich im Aushang.
    /// </summary>
    [Fact]
    public async Task Ohne_Termin_gibt_es_keinen_Spielplanvorschlag()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-7", OhneTermin(4, auslosen: true));

        var antwort = await aufbau.Admin.PostAsync(
            $"/api/tournaments/{aufbau.TournamentId}/schedule/proposal", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
        Assert.Contains("Termin", await antwort.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Abbruch ist kein Löschen: das Turnier bleibt mit allem, was gespielt
    /// wurde, lesbar — es wird nur nicht mehr fortgesetzt.
    /// </summary>
    [Fact]
    public async Task Ein_Turnier_lässt_sich_abbrechen()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-5", OhneTermin(4, auslosen: true));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/abandon", null)).StatusCode);

        var turnier = await TurnierAsync(aufbau.Admin, aufbau.TournamentId);

        Assert.Equal(TournamentState.Abandoned, turnier.State);
        Assert.NotEmpty(turnier.Entries);
    }

    /// <summary>
    /// Löschen ist das Gegenteil des Abbruchs: der beendet und lässt lesbar,
    /// dieses lässt nichts. Auch ein gespieltes Turnier geht — es ist der Weg
    /// für das, was gar nicht hätte entstehen sollen, und der Probelauf ist
    /// genau das.
    /// </summary>
    [Fact]
    public async Task Ein_Turnier_lässt_sich_löschen()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-8", OhneTermin(4, auslosen: true));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await aufbau.Admin.DeleteAsync($"/api/tournaments/{aufbau.TournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await aufbau.Admin.GetAsync($"/api/tournaments/{aufbau.TournamentId}")).StatusCode);

        var meine = await aufbau.Admin.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);
        Assert.DoesNotContain(meine!, t => t.Id == aufbau.TournamentId);
    }

    /// <summary>
    /// Was die Datenbank nicht als Abhängigkeit kennt, muss der Anwendungsfall
    /// räumen: die öffentliche Projektion trägt die Turnierkennung als eigene
    /// Id und hat keinen Fremdschlüssel. Bliebe sie stehen, wäre das Turnier
    /// nach dem Löschen öffentlich weiter abrufbar.
    /// </summary>
    [Fact]
    public async Task Nach_dem_Löschen_gibt_es_auch_die_öffentliche_Ansicht_nicht_mehr()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-9", OhneTermin(4, auslosen: true));

        // Die Projektion entsteht mit der Auslosung.
        Assert.Equal(
            HttpStatusCode.OK,
            (await aufbau.Admin.GetAsync($"/public/tournaments/{aufbau.TournamentId}")).StatusCode);

        await aufbau.Admin.DeleteAsync($"/api/tournaments/{aufbau.TournamentId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await aufbau.Admin.GetAsync($"/public/tournaments/{aufbau.TournamentId}")).StatusCode);
    }

    /// <summary>
    /// Ergebnisse eintragen heißt nicht, das Turnier abschaffen zu dürfen.
    /// </summary>
    [Fact]
    public async Task Ein_Schiedsrichter_loescht_kein_Turnier()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-10", OhneTermin(2, auslosen: false));

        var referee = $"referee-{Guid.NewGuid():N}";
        await _factory.GrantAsync(referee, Role.Referee, ResourceScope.Tournament(aufbau.TournamentId));

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _factory.CreateClientAs(referee)
                .DeleteAsync($"/api/tournaments/{aufbau.TournamentId}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await aufbau.Admin.GetAsync($"/api/tournaments/{aufbau.TournamentId}")).StatusCode);
    }

    /// <summary>
    /// Ein abgebrochenes Turnier ist am Ende. Ein zweiter Abbruch ist kein
    /// stiller Erfolg, sondern eine Absage — sonst sähe der Aufrufer eine
    /// Bestätigung für etwas, das nicht stattgefunden hat.
    /// </summary>
    [Fact]
    public async Task Ein_abgebrochenes_Turnier_lässt_sich_nicht_erneut_abbrechen()
    {
        var aufbau = await _factory.NeuesTurnierAsync("ablauf-6", OhneTermin(2, auslosen: false));

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/abandon", null);

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/abandon", null)).StatusCode);
    }
}
