using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Endpunkte, mit denen eine Turnierleitung ihr Feld und ihre Plätze
/// pflegt, nachdem beides schon einmal stand.
///
/// Das Anlegen führt jeder Test mit; das Wegnehmen und Nachtragen kommt erst
/// vor, wenn sich etwas geändert hat — ein Platz fällt aus, eine Zeit
/// verschiebt sich, jemand rückt von der Warteliste nach. Genau diese Wege
/// gingen ungeprüft, und es sind die, die am Turniertag gebraucht werden.
/// </summary>
public sealed class PlaetzeUndMeldungenApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public PlaetzeUndMeldungenApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<TournamentDetail> DetailAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<TournamentDetail>($"/api/tournaments/{tournamentId}", Json))!;

    [Fact]
    public async Task Ein_Platz_laesst_sich_wieder_entfernen()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Plaetze = 2, Auslosen = false });

        var response = await turnier.Admin.DeleteAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{turnier.CourtIds[1]}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await DetailAsync(turnier.Admin, turnier.TournamentId);
        Assert.Single(detail.Courts);
        Assert.DoesNotContain(detail.Courts, c => c.Id == turnier.CourtIds[1]);
    }

    /// <summary>
    /// Ein bespielter Platz lässt sich nicht entfernen.
    ///
    /// Das Aggregat sagt es selbst — „entfernen geht nur, solange keine
    /// Ansetzung darauf zeigt" —, und wissen kann das nur der Anwendungsfall.
    /// Ungeprüft war es kein stiller Fehler, sondern ein lauter am falschen
    /// Ort: die Beziehung ist Restrict, `SaveChanges` scheiterte, und der
    /// Veranstalter bekam eine 500 ohne einen Satz dazu, was zu tun ist.
    /// </summary>
    [Fact]
    public async Task Ein_Platz_mit_Ansetzungen_laesst_sich_nicht_entfernen()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 4, Plaetze = 2, Platzzeiten = true, Spielplan = true });

        var brett = await turnier.Admin.GetFromJsonAsync<MatchDayBoard>(
            $"/api/tournaments/{turnier.TournamentId}/courts", Json);

        var belegt = brett!.Courts.First(c => c.Queue.Count > 0).CourtId;

        var response = await turnier.Admin.DeleteAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{belegt}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Und der Weg, den die Meldung nennt, führt weiter: stilllegen geht.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await turnier.Admin.PutAsJsonAsync(
                $"/api/tournaments/{turnier.TournamentId}/courts/{belegt}",
                new UpdateCourtRequest("Platz 1", IsCenterCourt: false, IsActive: false),
                Json)).StatusCode);
    }

    [Fact]
    public async Task Eine_einzelne_Platzzeit_laesst_sich_nachtragen_und_wieder_streichen()
    {
        // Der Nachtrag am Rand: ein Platz, der am Sonntag erst ab Mittag frei
        // ist, kommt nicht aus der Massenanlage.
        var turnier = await _factory.NeuesTurnierAsync(
            $"platzwart-{Guid.NewGuid():N}",
            new TurnierWunsch { Plaetze = 1, Auslosen = false });

        var platz = turnier.CourtIds[0];
        var von = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.FromHours(2));

        var windowId = await CreatedIdAsync(await turnier.Admin.PostAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{platz}/windows",
            new CreateCourtWindowRequest(von, von.AddHours(6)),
            Json));

        var mit = await DetailAsync(turnier.Admin, turnier.TournamentId);
        Assert.Contains(mit.Courts.Single().Windows, w => w.Id == windowId);

        var response = await turnier.Admin.DeleteAsync(
            $"/api/tournaments/{turnier.TournamentId}/courts/{platz}/windows/{windowId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var ohne = await DetailAsync(turnier.Admin, turnier.TournamentId);
        Assert.DoesNotContain(ohne.Courts.Single().Windows, w => w.Id == windowId);
    }

    [Fact]
    public async Task Eine_Meldung_laesst_sich_auf_die_Warteliste_setzen()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 3, Auslosen = false });

        var response = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/{turnier.EntryIds[2]}/waiting-list",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await DetailAsync(turnier.Admin, turnier.TournamentId);
        var meldung = detail.Entries.Single(e => e.Id == turnier.EntryIds[2]);
        Assert.Equal(EntryStatus.WaitingList, meldung.Status);

        // Und wieder zurück ins Feld — dafür gibt es die Warteliste.
        var zurueck = await turnier.Admin.PostAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/{turnier.EntryIds[2]}/accept",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, zurueck.StatusCode);
    }

    [Fact]
    public async Task Eine_Setzposition_laesst_sich_setzen_und_wieder_nehmen()
    {
        var turnier = await _factory.NeuesTurnierAsync(
            $"leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Teilnehmer = 2, Setzen = false, Auslosen = false });

        var meldung = turnier.EntryIds[0];

        var gesetzt = await turnier.Admin.PutAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/{meldung}/seed",
            new SetSeedRequest(1),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, gesetzt.StatusCode);
        Assert.Equal(
            1,
            (await DetailAsync(turnier.Admin, turnier.TournamentId)).Entries
                .Single(e => e.Id == meldung).Seed);

        var genommen = await turnier.Admin.PutAsJsonAsync(
            $"/api/tournaments/{turnier.TournamentId}/entries/{meldung}/seed",
            new SetSeedRequest(null),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, genommen.StatusCode);
        Assert.Null(
            (await DetailAsync(turnier.Admin, turnier.TournamentId)).Entries
                .Single(e => e.Id == meldung).Seed);
    }

    [Fact]
    public async Task Eine_eigene_Formatvorlage_laesst_sich_anlegen()
    {
        // Der Weg neben dem Kopieren: wer von Grund auf eine eigene Vorlage
        // schreibt, braucht keinen Vorfahren.
        var client = _factory.CreateClientAs($"veranstalter-{Guid.NewGuid():N}");

        var definition = BuiltInFormats.Knockout with
        {
            Id = $"eigene-{Guid.NewGuid():N}",
            Name = "Eigenes K.-o.",
        };

        var id = await CreatedIdAsync(await client.PostAsJsonAsync(
            "/api/format-templates",
            new SaveFormatTemplateRequest(definition),
            Json));

        var detail = await client.GetFromJsonAsync<FormatTemplateDetail>(
            $"/api/format-templates/{id}", Json);

        Assert.NotNull(detail);
        Assert.False(detail.IsBuiltIn);
        Assert.Equal("Eigenes K.-o.", detail.Name);

        // Und sie steht in der Liste des Anlegers — sie gehört ihm.
        var liste = await client.GetFromJsonAsync<List<FormatTemplateSummary>>(
            "/api/format-templates", Json);

        Assert.Contains(liste!, v => v.Id == id);
    }
}
