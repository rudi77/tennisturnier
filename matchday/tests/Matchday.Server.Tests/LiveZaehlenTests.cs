using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Storage;
using Microsoft.Data.Sqlite;

namespace Matchday.Server.Tests;

/// <summary>Live zählen hinter der API: Sicht, Werkzeuge, Rechte und der Speicher.</summary>
public sealed class LiveZaehlenTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<(Tournament T, Guid Match)> Ausgelost(MatchFormat format)
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup", Format: format, Participants: ["Anna", "Tom"]));
        t = await _a.Actions.DrawAsync(_a.Rudi, t.Id);
        t = await _a.Actions.StartAsync(_a.Rudi, t.Id);
        return (t, t.Matches[0].Id);
    }

    private async Task<Tournament> Zaehle(Guid id, Guid match, string folge)
    {
        Tournament t = null!;

        foreach (var c in folge)
        {
            var request = c switch
            {
                '1' => new LiveRequest(LiveAction.Point, 1),
                '2' => new LiveRequest(LiveAction.Point, 2),
                'a' => new LiveRequest(LiveAction.Game, 1),
                _ => new LiveRequest(LiveAction.Game, 2),
            };
            t = await _a.Actions.LiveAsync(_a.Rudi, id, match, request);
        }

        return t;
    }

    [Fact]
    public async Task Die_Sicht_zeigt_den_laufenden_Satz_und_die_Ansage()
    {
        var (t, match) = await Ausgelost(MatchFormat.Standard);

        t = await Zaehle(t.Id, match, "aaaaaa" + "ab" + "11");
        var live = ViewBuilder.Build(t).Matches[0].Live!;
        Assert.Equal([new SetScore(6, 0), new SetScore(1, 1)], live.Sets);
        Assert.Equal(("30", "0"), (live.Points1, live.Points2));
        Assert.Contains("läuft: 6:0, 1:1, 30:0", AgentTools.Summarize(t));

        // Satz zu Ende, im nächsten noch nichts: kein leerer laufender Satz.
        t = await Zaehle(t.Id, match, "bbbbb");
        live = ViewBuilder.Build(t).Matches[0].Live!;
        Assert.Equal([new SetScore(6, 0), new SetScore(1, 6)], live.Sets);
        Assert.True(live.InMatchTiebreak);

        // Im Match-Tiebreak stehen die Punkte als laufender Satz.
        t = await Zaehle(t.Id, match, "112");
        live = ViewBuilder.Build(t).Matches[0].Live!;
        Assert.Equal(new SetScore(2, 1), live.Sets[^1]);
        Assert.Contains("Match-Tiebreak 2:1", AgentTools.Summarize(t));

        // Zu Ende gezählt: das Ergebnis, kein Live-Stand mehr.
        t = await Zaehle(t.Id, match, "11111111");
        var fertig = ViewBuilder.Build(t).Matches[0];
        Assert.Null(fertig.Live);
        Assert.Equal(MatchStatus.Finished, fertig.Status);
        Assert.Equal("6:0, 1:6, 10:1", fertig.Score!.Text);
    }

    [Fact]
    public async Task Im_Tiebreak_sagt_der_Agent_die_Punkte()
    {
        var (t, match) = await Ausgelost(new MatchFormat(BestOf: 1));
        t = await Zaehle(t.Id, match, string.Concat(Enumerable.Repeat("ab", 6)) + "12");
        Assert.Contains("6:6, Tiebreak 1:1", AgentTools.Summarize(t));
        Assert.True(ViewBuilder.Build(t).Matches[0].Live!.InTiebreak);
    }

    [Fact]
    public async Task Ein_Schritt_auf_altem_Stand_gilt_nicht()
    {
        // Zwei Handys am selben Match, oder ein nachgeschickter Schritt, dessen
        // Antwort verloren ging: Wer auf einem Stand zählt, den es nicht mehr
        // gibt, zählt nicht mit.
        var (t, match) = await Ausgelost(MatchFormat.Standard);

        await _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest(LiveAction.Point, 1, After: 0));
        await _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest(LiveAction.Point, 2, After: 1));

        await Assert.ThrowsAsync<ConflictException>(() => _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest(LiveAction.Point, 2, After: 1)));
        await Assert.ThrowsAsync<ConflictException>(() => _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest(LiveAction.Undo, After: 3)));

        var stand = await _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest(LiveAction.Undo, After: 2));
        Assert.Single(stand.FindMatch(match).Live!);
    }

    [Fact]
    public async Task Ein_unbekannter_Schritt_wird_abgewiesen()
    {
        var (t, match) = await Ausgelost(MatchFormat.Standard);
        await Assert.ThrowsAsync<DomainException>(() => _a.Actions.LiveAsync(_a.Rudi, t.Id, match, new LiveRequest((LiveAction)99)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _a.Actions.LiveAsync(_a.Rudi, t.Id, match, null!));
    }

    [Fact]
    public async Task Eintragen_darf_wer_den_Link_hat_und_sonst_niemand()
    {
        var (t, match) = await Ausgelost(MatchFormat.Standard);
        var helfer = new Actor("browser-helfer", null, t.ScorerToken);

        await _a.Actions.LiveAsync(helfer, t.Id, match, new LiveRequest(LiveAction.Point, 1));
        var gefunden = await _a.Actions.GetByScorerTokenAsync(t.ScorerToken);
        Assert.Equal(t.Id, gefunden.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => _a.Actions.LiveAsync(_a.Fremder, t.Id, match, new LiveRequest(LiveAction.Point, 1)));
        await Assert.ThrowsAsync<ForbiddenException>(() => _a.Actions.UndoDrawAsync(helfer, t.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _a.Actions.GetByScorerTokenAsync("gibt-es-nicht"));

        // Mit dem Verwalterlink rotiert auch der Eintragen-Link: Der alte gilt nicht mehr.
        var neu = await _a.Actions.RotateAdminTokenAsync(_a.Rudi, t.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => _a.Actions.LiveAsync(helfer, t.Id, match, new LiveRequest(LiveAction.Point, 1)));
        await Assert.ThrowsAsync<NotFoundException>(() => _a.Actions.GetByScorerTokenAsync(t.ScorerToken));
        Assert.Equal(t.Id, (await _a.Actions.GetByScorerTokenAsync(neu.ScorerToken)).Id);
    }

    [Fact]
    public async Task Die_Links_nennen_den_Eintragen_Link()
    {
        var (t, _) = await Ausgelost(MatchFormat.Standard);
        var geteilt = await _a.Tools.ExecuteAsync("share_links", JsonSerializer.SerializeToElement(new { }), _a.Rudi, t.Id, "https://matchday.test", CancellationToken.None);

        Assert.Contains($"https://matchday.test/?s={t.ScorerToken}", geteilt.ResultForModel);
        Assert.Equal($"https://matchday.test/?s={t.ScorerToken}", ViewBuilder.Links(t, "https://matchday.test").ScorerUrl);
    }
}

/// <summary>Eine Datenbank aus der Zeit vor dem Eintragen-Link und den Gesprächen je Turnier.</summary>
public sealed class SpeicherWanderungTests
{
    [Fact]
    public async Task Eine_alte_Datei_bekommt_die_neuen_Spalten_samt_Inhalt()
    {
        var pfad = Path.Combine(Path.GetTempPath(), $"matchday-alt-{Guid.NewGuid():N}.db");
        var verbindung = $"Data Source={pfad};Pooling=False";

        try
        {
            var t = Tournament.Create("Alter Cup", "browser-rudi", DateTimeOffset.UtcNow);
            var turnierJson = JsonSerializer.Serialize(t.ToSnapshot(), TournamentStore.Json);

            using (var alt = new SqliteConnection(verbindung))
            {
                alt.Open();
                using var befehl = alt.CreateCommand();
                befehl.CommandText = """
                    CREATE TABLE tournaments (id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, admin_token TEXT NOT NULL UNIQUE, json TEXT NOT NULL, updated_at TEXT NOT NULL);
                    CREATE TABLE sessions (id TEXT PRIMARY KEY, client_id TEXT NOT NULL, json TEXT NOT NULL, updated_at TEXT NOT NULL);
                    INSERT INTO tournaments VALUES ($id, 'browser-rudi', $token, $json, '2026-09-01');
                    INSERT INTO sessions VALUES ('s1', 'browser-rudi', $session, '2026-09-01');
                    """;
                befehl.Parameters.AddWithValue("$id", t.Id.ToString());
                befehl.Parameters.AddWithValue("$token", t.AdminToken);
                befehl.Parameters.AddWithValue("$json", turnierJson);
                befehl.Parameters.AddWithValue("$session", $$"""{"id":"s1","clientId":"browser-rudi","tournamentId":"{{t.Id}}","messages":[]}""");
                befehl.ExecuteNonQuery();
            }

            var store = new TournamentStore(verbindung);
            Assert.Equal(t.Id, (await store.FindByScorerTokenAsync(t.ScorerToken))!.Id);
            Assert.Contains("\"s1\"", await store.FindSessionJsonForTournamentAsync(t.Id, "browser-rudi"));

            // Ein zweiter Start findet die Spalten vor und lässt sie in Ruhe.
            var nochmal = new TournamentStore(verbindung);
            Assert.NotNull(await nochmal.FindByScorerTokenAsync(t.ScorerToken));
        }
        finally
        {
            Aufbau.Wegräumen(pfad);
        }
    }
}
