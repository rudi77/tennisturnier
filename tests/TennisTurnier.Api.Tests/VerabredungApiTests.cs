using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Membership;
using TennisTurnier.Application.Social;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Social;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Spielverabredungen außerhalb jedes Turniers (ADR-0015).
///
/// Sie sind kein Turnier mit einem Match: kein Draw, kein Ergebnis, kein
/// Zustandsautomat. Was hier geprüft wird, ist deshalb vor allem die
/// Sichtbarkeit — sie hängt an nichts, was es vorher schon gab, und ist damit
/// die einzige Stelle im System mit einem eigenen Query-Filter.
/// </summary>
public sealed class VerabredungApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public VerabredungApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Weit genug in der Zukunft, dass die gestellte Uhr der Fabrik sie nicht
    /// überholt — sie steht auf dem 16. Mai 2026.
    /// </summary>
    private static DateTimeOffset Termin => new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Wer_nichts_verabredet_hat_sieht_nichts()
    {
        var neu = _factory.CreateClientAs($"verabredung-{Guid.NewGuid():N}");

        Assert.Empty(await ListeAsync(neu));
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_keine_Verabredungen()
    {
        var antwort = await _factory.CreateClient().GetAsync("/api/play-dates");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Eine_Verabredung_zu_zweit_steht_mit_der_ersten_Zusage()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();

        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        // Der Gastgeber zählt mit — es fehlt genau einer.
        Assert.Equal(2, erstellt.RequiredPlayers);
        Assert.Equal(1, erstellt.Committed);
        Assert.Equal(1, erstellt.Missing);
        Assert.False(erstellt.IsConfirmed);
        Assert.True(erstellt.IsHost);

        var nachZusage = await AntwortenAsync(gast, erstellt.Id, accepted: true);

        Assert.Equal(2, nachZusage.Committed);
        Assert.Equal(0, nachZusage.Missing);
        Assert.True(nachZusage.IsConfirmed);
        Assert.False(nachZusage.IsHost);
        Assert.Equal(InvitationResponse.Accepted, nachZusage.MyResponse);
    }

    [Fact]
    public async Task Ein_Doppel_braucht_vier()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();

        var erstellt = await AnlegenAsync(gastgeber, Discipline.Doubles, [gastPlayerId]);

        Assert.Equal(4, erstellt.RequiredPlayers);
        Assert.Equal(3, erstellt.Missing);

        var nachZusage = await AntwortenAsync(gast, erstellt.Id, accepted: true);
        Assert.Equal(2, nachZusage.Missing);
        Assert.False(nachZusage.IsConfirmed);
    }

    [Fact]
    public async Task Eine_Absage_zaehlt_nicht_mit()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        var nachAbsage = await AntwortenAsync(gast, erstellt.Id, accepted: false);

        Assert.Equal(1, nachAbsage.Committed);
        Assert.False(nachAbsage.IsConfirmed);
        Assert.Equal(InvitationResponse.Declined, nachAbsage.MyResponse);
    }

    /// <summary>
    /// Der Kern von ADR-0015: die Verabredung hat kein Turnier, an dem ihre
    /// Sichtbarkeit hinge. Wer nicht eingeladen ist, sieht sie nicht — und
    /// kann sie auch nicht über ihre Id erreichen.
    /// </summary>
    [Fact]
    public async Task Wer_nicht_eingeladen_ist_sieht_die_Verabredung_nicht()
    {
        var (gastgeber, _, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        var fremder = _factory.CreateClientAs($"verabredung-fremd-{Guid.NewGuid():N}");

        Assert.Empty(await ListeAsync(fremder));

        var antwort = await fremder.PostAsJsonAsync(
            $"/api/play-dates/{erstellt.Id}/response", new RespondToPlayDateRequest(true), Json);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Der_Eingeladene_sieht_sie_in_seiner_Liste()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        var seine = Assert.Single(await ListeAsync(gast));

        Assert.Equal(erstellt.Id, seine.Id);
        Assert.False(seine.IsHost);
        Assert.Equal(InvitationResponse.Pending, seine.MyResponse);
    }

    [Fact]
    public async Task Nur_der_Gastgeber_sagt_ab()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        var vergeblich = await gast.DeleteAsync($"/api/play-dates/{erstellt.Id}");
        Assert.Equal(HttpStatusCode.NotFound, vergeblich.StatusCode);

        var abgesagt = await gastgeber.DeleteAsync($"/api/play-dates/{erstellt.Id}");
        Assert.Equal(HttpStatusCode.OK, abgesagt.StatusCode);

        var danach = (await abgesagt.Content.ReadFromJsonAsync<PlayDateView>(Json))!;
        Assert.True(danach.IsCancelled);
    }

    [Fact]
    public async Task Auf_eine_abgesagte_Verabredung_antwortet_niemand_mehr()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        await gastgeber.DeleteAsync($"/api/play-dates/{erstellt.Id}");

        var antwort = await gast.PostAsJsonAsync(
            $"/api/play-dates/{erstellt.Id}/response", new RespondToPlayDateRequest(true), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    /// <summary>
    /// Ein Spieler ohne Konto könnte weder zusagen noch die Einladung sehen.
    /// Die Einladung wird deshalb abgewiesen und nicht still übergangen — der
    /// Gastgeber wartete sonst auf eine Antwort, die niemand geben kann.
    /// </summary>
    [Fact]
    public async Task Wer_kein_Konto_hat_laesst_sich_nicht_einladen()
    {
        // Ein Mitspieler aus einem gespielten Match — er steht in den Kontakten
        // und lässt sich trotzdem nicht einladen, weil er nicht antworten kann.
        var (gastgeber, ohneKonto) = await KontakteOhneKontoAsync(1);

        var antwort = await gastgeber.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Samstag?", Discipline.Singles, "TC Test", Termin, 60, null, [.. ohneKonto]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
        Assert.Contains("Ohne Konto", await antwort.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Eingeladen wird aus den eigenen Mitspielern (ADR-0015).
    ///
    /// Der Grund steht in der Entscheidung daneben: ohne Kontaktgraph „gäbe es
    /// hier eine Suche über alle Benutzer, und die will niemand haben, der sich
    /// einmal überlegt hat, was sie preisgibt". Geprüft wurde es trotzdem nie —
    /// jeder Angemeldete konnte jeden mit Konto einladen und über die
    /// Fehlermeldung zu beliebigen Spieler-Ids Anzeigenamen erfahren.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Wer_kein_Mitspieler_ist_laesst_sich_nicht_einladen(int fremde)
    {
        var ids = new List<Guid>();

        for (var i = 0; i < fremde; i++)
        {
            var (_, _, playerId) = await ZweiSpielerAsync();
            ids.Add(playerId);
        }

        // Ein Angemeldeter, der mit niemandem gespielt hat.
        var fremder = _factory.CreateClientAs($"fremder-{Guid.NewGuid():N}");

        var antwort = await fremder.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Samstag?", Discipline.Doubles, "TC Test", Termin, 60, null, ids),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);

        var auskunft = await antwort.Content.ReadAsStringAsync();

        Assert.Contains("eigenen Mitspielern", auskunft, StringComparison.Ordinal);

        // Und kein Name: zu einer beliebigen Spieler-Id den Anzeigenamen zu
        // erfahren, ist genau die Auskunft, die der Kontaktgraph verhindert.
        Assert.DoesNotContain("Berta", auskunft, StringComparison.Ordinal);
    }

    /// <summary>
    /// Der Partner aus einem Doppel ist ein Mitspieler wie jeder Gegner.
    ///
    /// Er steht auf derselben Seite und wird trotzdem gezählt — ein Doppel
    /// bringt drei Verbindungen mit einem Match (ADR-0013), und aus allen
    /// dreien lässt sich einladen.
    /// </summary>
    [Fact]
    public async Task Auch_der_Partner_aus_einem_Doppel_laesst_sich_einladen()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"verabredung-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Name = "Doppelturnier",
                Disziplin = Discipline.Doubles,
                Auslosen = false,
            });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        var gastgeber = await BeitretenAsync(link!.Token, "Anna", partner: "Paul");
        await BeitretenAsync(link.Token, "Berta", partner: "Bernd");

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        foreach (var meldung in meldungen!)
        {
            await aufbau.Admin.PostAsync(
                $"/api/tournaments/{aufbau.TournamentId}/entries/{meldung.Id}/accept", null);
        }

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/close", null);
        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/draw", null);

        var phasen = await aufbau.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{aufbau.TournamentId}/phases", Json);

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{phasen!.SelectMany(p => p.Matches).Single().Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
            Json);

        var kontakte = await gastgeber.GetFromJsonAsync<List<ConnectionView>>(
            "/api/me/connections", Json);

        // Drei Verbindungen aus einem Doppel: der Partner und die zwei Gegner.
        Assert.Equal(3, kontakte!.Count);

        // Und der Partner — die einzige Verbindung ohne ein Spiel gegeneinander.
        var partner = kontakte.Single(k => k.Against == 0);

        var antwort = await gastgeber.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Samstag?", Discipline.Singles, "TC Test", Termin, 60, null, [partner.PlayerId]),
            Json);

        // Ohne Konto lässt er sich nicht einladen — aber abgewiesen wird er
        // dafür und nicht dafür, kein Mitspieler zu sein.
        var auskunft = await antwort.Content.ReadAsStringAsync();

        Assert.DoesNotContain("eigenen Mitspielern", auskunft, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auch_nachtraeglich_wird_nur_aus_den_eigenen_Mitspielern_eingeladen()
    {
        var (gastgeber, _, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Doubles, [gastPlayerId]);

        // Jemand, mit dem der Gastgeber nie gespielt hat.
        var (_, _, fremderPlayerId) = await ZweiSpielerAsync();

        var antwort = await gastgeber.PostAsJsonAsync(
            $"/api/play-dates/{erstellt.Id}/invitations",
            new InviteToPlayDateRequest([fremderPlayerId]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_Termin_in_der_Vergangenheit_wird_abgewiesen()
    {
        var (gastgeber, _, gastPlayerId) = await ZweiSpielerAsync();

        var antwort = await gastgeber.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Gestern?",
                Discipline.Singles,
                "TC Test",
                new DateTimeOffset(2020, 1, 1, 9, 0, 0, TimeSpan.Zero),
                60,
                null,
                [gastPlayerId]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    [Fact]
    public async Task Die_Kontaktliste_sagt_wer_sich_einladen_laesst()
    {
        var (gastgeber, _, _) = await ZweiSpielerAsync();

        var kontakte = await gastgeber.GetFromJsonAsync<List<ConnectionView>>(
            "/api/me/connections", Json);

        Assert.All(kontakte!, kontakt => Assert.True(kontakt.CanBeInvited));
    }

    /// <summary>
    /// Ohne Eingeladene ist eine Verabredung ein Merkzettel — zulässig, und der
    /// Gastgeber lädt später ein. Genau dieser Weg braucht keinen Spieler zum
    /// Konto: wer noch nie gemeldet war, hat keinen.
    /// </summary>
    [Fact]
    public async Task Eine_Verabredung_ohne_Eingeladene_ist_zulaessig()
    {
        var allein = _factory.CreateClientAs($"verabredung-allein-{Guid.NewGuid():N}");

        var erstellt = await AnlegenAsync(allein, Discipline.Singles, []);

        Assert.Empty(erstellt.Guests);
        Assert.Equal(1, erstellt.Missing);
        Assert.True(erstellt.IsHost);

        // Ein Gastgeber ohne Spieler bleibt ein Name — es gibt kein Profil,
        // auf das ein Verweis zeigen könnte.
        Assert.Null(erstellt.Host.PlayerId);
    }

    [Fact]
    public async Task Der_Gastgeber_laedt_auch_spaeter_noch_ein()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, []);

        var antwort = await gastgeber.PostAsJsonAsync(
            $"/api/play-dates/{erstellt.Id}/invitations",
            new InviteToPlayDateRequest([gastPlayerId]),
            Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        var danach = (await antwort.Content.ReadFromJsonAsync<PlayDateView>(Json))!;
        Assert.Single(danach.Guests);

        // Und der Eingeladene sieht sie jetzt.
        Assert.Contains(await ListeAsync(gast), date => date.Id == erstellt.Id);
    }

    [Fact]
    public async Task Nur_der_Gastgeber_laedt_ein()
    {
        var (gastgeber, gast, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        var antwort = await gast.PostAsJsonAsync(
            $"/api/play-dates/{erstellt.Id}/invitations",
            new InviteToPlayDateRequest([gastPlayerId]),
            Json);

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(600)]
    public async Task Eine_unsinnige_Dauer_wird_abgewiesen(int minuten)
    {
        var (gastgeber, _, gastPlayerId) = await ZweiSpielerAsync();

        var antwort = await gastgeber.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Kurz?", Discipline.Singles, "TC Test", Termin, minuten, null, [gastPlayerId]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    /// <summary>
    /// Zwei ohne Konto lesen sich anders als einer — die Meldung nennt beide
    /// und sagt „haben keines".
    /// </summary>
    [Fact]
    public async Task Zwei_ohne_Konto_werden_beide_genannt()
    {
        var (gastgeber, ohneKonto) = await KontakteOhneKontoAsync(2);

        var antwort = await gastgeber.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Samstag?", Discipline.Doubles, "TC Test", Termin, 90, null, [.. ohneKonto]),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
        Assert.Contains("haben keines", await antwort.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Eine vergangene Verabredung fällt aus der Liste — sie ist keine mehr.
    /// Wer zurückschauen will, sagt es ausdrücklich.
    /// </summary>
    [Fact]
    public async Task Vergangenes_steht_nur_auf_Nachfrage_in_der_Liste()
    {
        var (gastgeber, _, gastPlayerId) = await ZweiSpielerAsync();
        var erstellt = await AnlegenAsync(gastgeber, Discipline.Singles, [gastPlayerId]);

        // Die Uhr der Fabrik hinter den Termin stellen: die Verabredung ist
        // damit vorbei, ohne dass jemand etwas geändert hätte.
        var vorher = _factory.Clock.Now;
        _factory.Clock.Now = Termin.AddDays(1);

        try
        {
            Assert.DoesNotContain(await ListeAsync(gastgeber), date => date.Id == erstellt.Id);

            var mitVergangenem = (await gastgeber.GetFromJsonAsync<List<PlayDateView>>(
                "/api/play-dates?includePast=true", Json))!;

            var vergangen = Assert.Single(mitVergangenem, date => date.Id == erstellt.Id);
            Assert.True(vergangen.IsPast);
        }
        finally
        {
            _factory.Clock.Now = vorher;
        }
    }

    // --- Aufbau -----------------------------------------------------------

    /// <summary>
    /// Zwei Konten, die schon einmal gegeneinander gespielt haben — der
    /// Normalfall, aus dem eine Verabredung entsteht: eingeladen wird aus dem
    /// Kontaktgraphen (ADR-0015).
    /// </summary>
    private async Task<(HttpClient Gastgeber, HttpClient Gast, Guid GastPlayerId)> ZweiSpielerAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"verabredung-leitung-{Guid.NewGuid():N}",
            new TurnierWunsch { Name = "Verabredungsturnier", Auslosen = false });

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

        var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
            $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

        var gastgeber = await BeitretenAsync(link!.Token, "Anna");
        var gast = await BeitretenAsync(link.Token, "Berta");

        // Erst ein gespieltes Match macht aus den beiden Kontakte — und erst
        // dann steht der Gast in einer Liste, aus der eingeladen wird.
        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        foreach (var meldung in meldungen!)
        {
            await aufbau.Admin.PostAsync(
                $"/api/tournaments/{aufbau.TournamentId}/entries/{meldung.Id}/accept", null);
        }

        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/registration/close", null);
        await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/draw", null);

        // Und einmal gegeneinander gespielt: eingeladen wird aus dem
        // Kontaktgraphen (ADR-0015), und der entsteht aus Ergebnissen. Ohne
        // dieses Match wären die beiden füreinander Fremde.
        var phasen = await aufbau.Admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{aufbau.TournamentId}/phases", Json);

        var match = phasen!.SelectMany(p => p.Matches).Single();

        await aufbau.Admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
            Json);

        var kontakte = await gastgeber.GetFromJsonAsync<List<ConnectionView>>(
            "/api/me/connections", Json);

        var gastProfil = await gast.GetFromJsonAsync<PlayerProfileView>("/api/me/profile", Json);

        // Der Gast steht jetzt in der Liste, aus der eingeladen wird.
        Assert.Contains(kontakte!, k => k.PlayerId == gastProfil!.PlayerId && k.CanBeInvited);

        return (gastgeber, gast, gastProfil!.PlayerId);
    }

    /// <summary>
    /// Ein Gastgeber und so viele Mitspieler ohne Konto, wie verlangt.
    ///
    /// Je Mitspieler ein eigenes Turnier: der Gastgeber tritt bei, spielt gegen
    /// einen von der Turnierleitung angelegten Spieler und hat ihn danach in
    /// seinen Kontakten — mit <c>CanBeInvited = false</c>, weil hinter ihm kein
    /// Konto steht.
    ///
    /// Genau diese Sorte Kontakt meint ADR-0015 mit „die Kontaktliste sagt es
    /// vorher": jemand, mit dem man gespielt hat und den man trotzdem nicht
    /// einladen kann.
    /// </summary>
    private async Task<(HttpClient Gastgeber, IReadOnlyList<Guid> OhneKonto)> KontakteOhneKontoAsync(
        int anzahl)
    {
        HttpClient? gastgeber = null;
        var ohneKonto = new List<Guid>();

        for (var i = 0; i < anzahl; i++)
        {
            var aufbau = await _factory.NeuesTurnierAsync(
                $"verabredung-leitung-{Guid.NewGuid():N}",
                new TurnierWunsch { Name = "Verabredungsturnier", Teilnehmer = 1, Auslosen = false });

            // Vor dem Beitritt gelesen: die eine Meldung ist die des Spielers,
            // den die Turnierleitung angelegt hat.
            var vorher = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
                $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

            ohneKonto.Add(Assert.Single(vorher!).Contacts[0].PlayerId);

            await aufbau.Admin.PostAsync(
                $"/api/tournaments/{aufbau.TournamentId}/registration/open", null);

            var link = await aufbau.Admin.GetFromJsonAsync<RegistrationDetail>(
                $"/api/tournaments/{aufbau.TournamentId}/registration", Json);

            gastgeber ??= await BeitretenAsync(link!.Token, "Anna");

            if (i > 0)
            {
                // Derselbe Mensch tritt jedem weiteren Turnier bei.
                await gastgeber.PostAsJsonAsync(
                    $"/api/join/{link!.Token}",
                    new JoinRequest(Play: true, null, null, null, null, null, null, null),
                    Json);
            }

            var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
                $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

            foreach (var meldung in meldungen!)
            {
                await aufbau.Admin.PostAsync(
                    $"/api/tournaments/{aufbau.TournamentId}/entries/{meldung.Id}/accept", null);
            }

            await aufbau.Admin.PostAsync(
                $"/api/tournaments/{aufbau.TournamentId}/registration/close", null);
            await aufbau.Admin.PostAsync($"/api/tournaments/{aufbau.TournamentId}/draw", null);

            var phasen = await aufbau.Admin.GetFromJsonAsync<List<PhaseDetail>>(
                $"/api/tournaments/{aufbau.TournamentId}/phases", Json);

            await aufbau.Admin.PutAsJsonAsync(
                $"/api/matches/{phasen!.SelectMany(p => p.Matches).Single().Id}/result",
                new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 3)]),
                Json);
        }

        var kontakte = await gastgeber!.GetFromJsonAsync<List<ConnectionView>>(
            "/api/me/connections", Json);

        Assert.All(ohneKonto, id => Assert.Contains(kontakte!, k => k.PlayerId == id && !k.CanBeInvited));

        return (gastgeber!, ohneKonto);
    }

    private async Task<HttpClient> BeitretenAsync(string token, string vorname, string? partner = null)
    {
        var nachname = $"V{Guid.NewGuid():N}"[..10];

        var client = _factory.CreateClientAs(
            $"verabredung-{Guid.NewGuid():N}", $"{nachname.ToLowerInvariant()}@example.invalid");

        var antwort = await client.PostAsJsonAsync(
            $"/api/join/{token}",
            new JoinRequest(
                Play: true,
                vorname,
                nachname,
                null,
                partner,
                partner is null ? null : $"P{Guid.NewGuid():N}"[..10],
                null,
                null),
            Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return client;
    }

    private static async Task<PlayDateView> AnlegenAsync(
        HttpClient client,
        Discipline disziplin,
        IReadOnlyList<Guid> eingeladene)
    {
        var antwort = await client.PostAsJsonAsync(
            "/api/play-dates",
            new CreatePlayDateRequest(
                "Samstag eine Runde?",
                disziplin,
                "TC Musterstadt, Platz 2",
                Termin,
                60,
                "Bringt Bälle mit.",
                eingeladene),
            Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return (await antwort.Content.ReadFromJsonAsync<PlayDateView>(Json))!;
    }

    private static async Task<PlayDateView> AntwortenAsync(
        HttpClient client,
        Guid playDateId,
        bool accepted)
    {
        var antwort = await client.PostAsJsonAsync(
            $"/api/play-dates/{playDateId}/response", new RespondToPlayDateRequest(accepted), Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());

        return (await antwort.Content.ReadFromJsonAsync<PlayDateView>(Json))!;
    }

    private static async Task<List<PlayDateView>> ListeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PlayDateView>>("/api/play-dates", Json))!;
}
