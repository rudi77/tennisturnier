using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Social;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Das Spielerprofil (ADR-0013).
///
/// Die Bilanz wird nicht gespeichert, sondern gerechnet — über die Turniere,
/// die der Fragende ohnehin sehen darf. Was hier geprüft wird, ist deshalb
/// zweierlei: dass die Rechnung stimmt, und dass sie an der
/// Sichtbarkeitsgrenze endet. Das Zweite ist das Wichtigere: ein Profil, das
/// über den Query-Filter hinausreicht, wäre ein Fenster in fremde Turniere.
/// </summary>
public sealed class SpielerprofilApiTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public SpielerprofilApiTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Ein ausgespieltes Viererfeld: Runde eins entschieden, das Finale dazu.
    /// Der Sieger von Match eins gewinnt auch das Finale — damit steht eine
    /// Bilanz da, die sich nachrechnen lässt.
    /// </summary>
    private async Task<(HttpClient Leitung, Guid TournamentId, Guid SiegerPlayerId, Guid GegnerPlayerId)>
        AusgespieltAsync(string benutzer)
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            benutzer, new TurnierWunsch { Teilnehmer = 4, Kontaktdaten = true });

        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var erste = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();

        foreach (var match in erste)
        {
            await ErgebnisAsync(aufbau.Admin, match.Id, new SetScore(6, 4), new SetScore(6, 2));
        }

        var nachRundeEins = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));

        // Nach Position und nicht nach Runde allein: eine Vorlage darf neben dem
        // Finale ein Spiel um Platz 3 vorsehen, und das steht in derselben Runde.
        var finale = FinaleOf(nachRundeEins);

        await ErgebnisAsync(aufbau.Admin, finale.Id, new SetScore(7, 5), new SetScore(6, 3));

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var abgeschlossen = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var entschieden = FinaleOf(abgeschlossen);
        var sieger = entschieden.Score!.WinnerSide == 1 ? entschieden.Side1 : entschieden.Side2;
        var verlierer = entschieden.Score.WinnerSide == 1 ? entschieden.Side2 : entschieden.Side1;

        return (
            aufbau.Admin,
            aufbau.TournamentId,
            PlayerOf(meldungen!, sieger.EntryId!.Value),
            PlayerOf(meldungen!, verlierer.EntryId!.Value));
    }

    [Fact]
    public async Task Die_Bilanz_zaehlt_gewonnene_und_verlorene_Matches()
    {
        var (leitung, _, siegerId, _) = await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var profil = await ProfilAsync(leitung, siegerId);

        // Zwei Matches, beide gewonnen, vier Sätze zu null.
        Assert.Equal(2, profil.Record.Played);
        Assert.Equal(2, profil.Record.Won);
        Assert.Equal(0, profil.Record.Lost);
        Assert.Equal(4, profil.Record.SetsWon);
        Assert.Equal(0, profil.Record.SetsLost);
        Assert.Equal(1, profil.Record.Tournaments);
    }

    [Fact]
    public async Task Der_Unterlegene_des_Finales_hat_einen_Sieg_und_eine_Niederlage()
    {
        var (leitung, _, _, gegnerId) = await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var profil = await ProfilAsync(leitung, gegnerId);

        Assert.Equal(2, profil.Record.Played);
        Assert.Equal(1, profil.Record.Won);
        Assert.Equal(1, profil.Record.Lost);
    }

    [Fact]
    public async Task Das_Profil_nennt_Turnier_Gegner_und_Spielstand()
    {
        var (leitung, tournamentId, siegerId, gegnerId) =
            await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var profil = await ProfilAsync(leitung, siegerId);
        var finale = profil.Matches.First();

        Assert.Equal(tournamentId, finale.TournamentId);
        Assert.True(finale.Won);
        Assert.Equal("7:5 6:3", finale.Score);
        Assert.Contains(finale.Opponents, o => o.PlayerId == gegnerId);

        // Ein Einzel hat keinen Partner — das ist die einzige Stelle, an der ein
        // Profil den Unterschied zum Doppel überhaupt bemerkt.
        Assert.Null(finale.Partner);
    }

    /// <summary>
    /// Der Kern von ADR-0013: die Rechnung endet an der Sichtbarkeitsgrenze.
    ///
    /// Ein Fremder, der denselben Spieler abfragt, teilt mit ihm kein sichtbares
    /// Turnier — und bekommt deshalb 404 und nicht etwa ein leeres Profil. Ein
    /// 403 stünde hier falsch: es verriete, dass es diesen Spieler gibt
    /// (ADR-0004).
    /// </summary>
    [Fact]
    public async Task Wer_kein_Turnier_teilt_findet_das_Profil_nicht()
    {
        var (_, _, siegerId, _) = await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var fremder = _factory.CreateClientAs($"fremd-{Guid.NewGuid():N}");
        var antwort = await fremder.GetAsync($"/api/players/{siegerId}/profile");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Einen_Spieler_den_es_nicht_gibt_findet_niemand()
    {
        var leitung = _factory.CreateClientAs($"profil-{Guid.NewGuid():N}");

        var antwort = await leitung.GetAsync($"/api/players/{Guid.NewGuid()}/profile");

        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }

    [Fact]
    public async Task Ohne_Anmeldung_gibt_es_kein_Profil()
    {
        var (_, _, siegerId, _) = await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var anonym = _factory.CreateClient();
        var antwort = await anonym.GetAsync($"/api/players/{siegerId}/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    [Fact]
    public async Task Ein_Turnier_ohne_gespieltes_Match_steht_trotzdem_im_Profil()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"profil-{Guid.NewGuid():N}", new TurnierWunsch { Teilnehmer = 4, Auslosen = false });

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var spielerId = meldungen![0].Contacts[0].PlayerId;
        var profil = await ProfilAsync(aufbau.Admin, spielerId);

        // Gemeldet und nie gespielt: das Turnier zählt, die Bilanz bleibt leer.
        Assert.Equal(0, profil.Record.Played);
        Assert.Equal(1, profil.Record.Tournaments);
        Assert.Equal(aufbau.TournamentId, Assert.Single(profil.Tournaments).TournamentId);
    }

    [Fact]
    public async Task Wer_noch_nie_gemeldet_hat_hat_kein_eigenes_Profil()
    {
        var neu = _factory.CreateClientAs($"profil-neu-{Guid.NewGuid():N}");

        var antwort = await neu.GetAsync("/api/me/profile");

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    /// <summary>
    /// Das Profil ist für viele die erste Stelle, an der überhaupt ein Spieler
    /// zu ihrem Konto entsteht — wer beigetreten ist, ohne zu melden, hat bis
    /// dahin keinen.
    /// </summary>
    [Fact]
    public async Task Das_erste_Speichern_legt_den_eigenen_Spieler_an()
    {
        var neu = _factory.CreateClientAs($"profil-neu-{Guid.NewGuid():N}");

        var antwort = await neu.PutAsJsonAsync(
            "/api/me/profile",
            new UpdateMyProfileRequest("Anna", "Vogel", "Spielt seit 2009.", "TC Hinterbrühl"),
            Json);

        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);

        var profil = (await antwort.Content.ReadFromJsonAsync<PlayerProfileView>(Json))!;

        Assert.True(profil.IsSelf);
        Assert.True(profil.HasAccount);
        Assert.Equal("Vogel, Anna", profil.DisplayName);
        Assert.Equal("Spielt seit 2009.", profil.Bio);
        Assert.Equal("TC Hinterbrühl", profil.HomeClub);

        // Und danach steht es unter der eigenen Adresse, ohne dass jemand die
        // Spieler-Id kennen müsste.
        var erneut = await neu.GetFromJsonAsync<PlayerProfileView>("/api/me/profile", Json);
        Assert.Equal(profil.PlayerId, erneut!.PlayerId);
    }

    [Fact]
    public async Task Ein_zu_langer_Text_ueber_sich_wird_abgewiesen()
    {
        var neu = _factory.CreateClientAs($"profil-neu-{Guid.NewGuid():N}");

        var antwort = await neu.PutAsJsonAsync(
            "/api/me/profile",
            new UpdateMyProfileRequest("Anna", "Vogel", new string('x', 501), null),
            Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, antwort.StatusCode);
    }

    /// <summary>
    /// Ein eingelesener Spieler gehört keinem Konto. Wer ihn ansieht, sieht
    /// seine Historie — bearbeiten kann sie niemand, auch die Turnierleitung
    /// nicht, die ihn angelegt hat.
    /// </summary>
    [Fact]
    public async Task Ein_Spieler_ohne_Konto_hat_eine_Historie_aber_kein_eigenes_Wort()
    {
        var (leitung, _, siegerId, _) = await AusgespieltAsync($"profil-{Guid.NewGuid():N}");

        var profil = await ProfilAsync(leitung, siegerId);

        Assert.False(profil.HasAccount);
        Assert.False(profil.IsSelf);
        Assert.Null(profil.Bio);
        Assert.NotEmpty(profil.Matches);
    }

    private static MatchDetail FinaleOf(PhaseDetail phase) =>
        phase.Matches.Where(m => m.Round == phase.Matches.Max(x => x.Round))
            .OrderBy(m => m.Position)
            .First();

    /// <summary>
    /// Das zweite Speichern ändert den Namen mit — es ist der eigene, und wer
    /// heiratet, heißt danach anders. Die Tabelle eines abgeschlossenen
    /// Turniers bleibt davon unberührt: der Anzeigename eines Teilnehmers wird
    /// beim Melden festgeschrieben.
    /// </summary>
    [Fact]
    public async Task Das_zweite_Speichern_berichtigt_den_Namen()
    {
        var neu = _factory.CreateClientAs($"profil-neu-{Guid.NewGuid():N}");

        await neu.PutAsJsonAsync(
            "/api/me/profile",
            new UpdateMyProfileRequest("Anna", "Vogel", null, null),
            Json);

        var zweites = await neu.PutAsJsonAsync(
            "/api/me/profile",
            new UpdateMyProfileRequest("Anna", "Vogel-Berger", "Jetzt mit Doppelnamen.", null),
            Json);

        var profil = (await zweites.Content.ReadFromJsonAsync<PlayerProfileView>(Json))!;

        Assert.Equal("Vogel-Berger, Anna", profil.DisplayName);
        Assert.Equal("Jetzt mit Doppelnamen.", profil.Bio);
    }

    [Fact]
    public async Task Das_eigene_Profil_steht_auch_unter_der_Spieler_Adresse()
    {
        var neu = _factory.CreateClientAs($"profil-neu-{Guid.NewGuid():N}");

        var angelegt = (await (await neu.PutAsJsonAsync(
            "/api/me/profile",
            new UpdateMyProfileRequest("Anna", "Vogel", null, null),
            Json)).Content.ReadFromJsonAsync<PlayerProfileView>(Json))!;

        // Man teilt mit sich selbst jedes eigene Turnier — auch wenn man noch
        // keines gespielt hat. Ein 404 stünde hier falsch.
        var profil = await ProfilAsync(neu, angelegt.PlayerId);

        Assert.True(profil.IsSelf);
        Assert.Empty(profil.Tournaments);
    }

    /// <summary>
    /// Ein Nichtantreten hat keinen Spielstand. Dort steht das Wort und nicht
    /// ein leerer Platz, der wie ein fehlendes Ergebnis aussähe.
    /// </summary>
    [Fact]
    public async Task Kampflos_Aufgabe_und_Disqualifikation_stehen_als_Wort()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"profil-{Guid.NewGuid():N}", new TurnierWunsch { Teilnehmer = 8, Kontaktdaten = true });

        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var erste = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).ToList();

        await Ergebnis(aufbau.Admin, erste[0].Id,
            new RecordResultRequest(MatchOutcome.Walkover, AffectedSide: 2));
        await Ergebnis(aufbau.Admin, erste[1].Id,
            new RecordResultRequest(MatchOutcome.Disqualification, AffectedSide: 2));
        await Ergebnis(aufbau.Admin, erste[2].Id, new RecordResultRequest(
            MatchOutcome.Retirement,
            Sets: [new SetScore(6, 4)],
            AbandonedSet: new SetScore(2, 1),
            AffectedSide: 2));

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var staende = new List<string>();

        foreach (var match in erste.Take(3))
        {
            var spieler = PlayerOf(meldungen!, match.Side1.EntryId!.Value);
            staende.AddRange((await ProfilAsync(aufbau.Admin, spieler)).Matches.Select(m => m.Score));
        }

        Assert.Contains("kampflos", staende);
        Assert.Contains("Disqualifikation", staende);
        Assert.Contains(staende, stand => stand.EndsWith("(Aufgabe)", StringComparison.Ordinal));
    }

    /// <summary>
    /// Lief das Match über einen Platz, kommt die Uhrzeit von dort — und mit
    /// ihr das Datum in der Bilanz.
    /// </summary>
    [Fact]
    public async Task Ein_Match_am_Platz_traegt_seine_Uhrzeit_ins_Profil()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"profil-{Guid.NewGuid():N}",
            new TurnierWunsch
            {
                Teilnehmer = 4,
                Kontaktdaten = true,
                Plaetze = 2,
                Platzzeiten = true,
                Spielplan = true,
                Turniertag = true,
                Uhr = new DateTimeOffset(2026, 5, 16, 8, 0, 0, TimeSpan.FromHours(2)),
            });

        var board = await aufbau.Admin.GetFromJsonAsync<List<CourtBoard>>(
            $"/api/tournaments/{aufbau.TournamentId}/courts", Json);

        var slot = board!.SelectMany(court => court.Queue).First();

        await aufbau.Admin.PostAsync($"/api/assignments/{slot.AssignmentId}/call", null);
        await aufbau.Admin.PostAsync($"/api/assignments/{slot.AssignmentId}/start", null);
        await aufbau.Admin.PostAsync($"/api/assignments/{slot.AssignmentId}/finish", null);

        await Ergebnis(aufbau.Admin, slot.MatchId,
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]));

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Single(m => m.Id == slot.MatchId);

        var profil = await ProfilAsync(aufbau.Admin, PlayerOf(meldungen!, match.Side1.EntryId!.Value));

        Assert.NotNull(profil.Matches[0].PlayedAt);
        Assert.NotNull(profil.Record.LastPlayedOn);
    }

    /// <summary>
    /// Im Doppel steht neben dem Gegner der Partner — die einzige Stelle, an
    /// der ein Profil den Unterschied zum Einzel überhaupt bemerkt.
    /// </summary>
    [Fact]
    public async Task Ein_Doppel_nennt_den_Partner()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"profil-{Guid.NewGuid():N}",
            new TurnierWunsch { Teams = ["Die Netzroller", "Die Grundlinie"], Kontaktdaten = true });

        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Single(m => m.Status == MatchStatus.Ready);

        await Ergebnis(aufbau.Admin, match.Id,
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]));

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var meldung = meldungen!.Single(e => e.Id == match.Side1.EntryId);
        var profil = await ProfilAsync(aufbau.Admin, meldung.Contacts[0].PlayerId);

        var eintrag = Assert.Single(profil.Matches);

        Assert.NotNull(eintrag.Partner);
        Assert.Equal(meldung.Contacts[1].PlayerId, eintrag.Partner!.PlayerId);
        Assert.Equal(2, eintrag.Opponents.Count);
    }

    /// <summary>
    /// Wer sofort aufgibt, hinterlässt keinen Satz. Dann steht dort das Wort
    /// allein und keine leere Klammer.
    /// </summary>
    [Fact]
    public async Task Eine_Aufgabe_ohne_gespielten_Satz_steht_als_Wort()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            $"profil-{Guid.NewGuid():N}", new TurnierWunsch { Teilnehmer = 4, Kontaktdaten = true });

        var phase = Assert.Single(await PhasenAsync(aufbau.Admin, aufbau.TournamentId));
        var match = phase.Matches.Where(m => m.Round == 1).OrderBy(m => m.Position).First();

        await Ergebnis(aufbau.Admin, match.Id,
            new RecordResultRequest(MatchOutcome.Retirement, AffectedSide: 2));

        var meldungen = await aufbau.Admin.GetFromJsonAsync<List<EntryOverview>>(
            $"/api/tournaments/{aufbau.TournamentId}/entries", Json);

        var profil = await ProfilAsync(aufbau.Admin, PlayerOf(meldungen!, match.Side1.EntryId!.Value));

        Assert.Equal("Aufgabe", Assert.Single(profil.Matches).Score);
    }

    private static async Task Ergebnis(HttpClient client, Guid matchId, RecordResultRequest request)
    {
        var antwort = await client.PutAsJsonAsync($"/api/matches/{matchId}/result", request, Json);

        Assert.True(antwort.IsSuccessStatusCode, await antwort.Content.ReadAsStringAsync());
    }

    private static Guid PlayerOf(IReadOnlyList<EntryOverview> entries, Guid entryId) =>
        entries.Single(e => e.Id == entryId).Contacts[0].PlayerId;

    private static async Task<PlayerProfileView> ProfilAsync(HttpClient client, Guid playerId) =>
        (await client.GetFromJsonAsync<PlayerProfileView>($"/api/players/{playerId}/profile", Json))!;

    private static async Task ErgebnisAsync(HttpClient client, Guid matchId, params SetScore[] sets)
    {
        var antwort = await client.PutAsJsonAsync(
            $"/api/matches/{matchId}/result",
            new RecordResultRequest(MatchOutcome.Normal, sets),
            Json);

        Assert.Equal(HttpStatusCode.NoContent, antwort.StatusCode);
    }

    private static async Task<List<PhaseDetail>> PhasenAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json))!;
}
