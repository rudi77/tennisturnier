using System.Text.Json;
using Matchday.Domain;
using Matchday.Server.Agent;
using Matchday.Server.Api;

namespace Matchday.Server.Tests;

/// <summary>Die Werkzeuge ohne Modell: was das Modell rufen würde, rufen wir direkt.</summary>
public sealed class AgentToolsTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    private static JsonElement In(object input) => JsonSerializer.SerializeToElement(input);

    private Task<ToolOutcome> Run(string name, object input, Guid? current = null) =>
        _a.Tools.ExecuteAsync(name, In(input), _a.Rudi, current, "https://matchday.test", CancellationToken.None);

    [Fact]
    public void Jedes_Werkzeug_hat_ein_gueltiges_Schema()
    {
        Assert.Equal(12, AgentTools.Definitions.Count);

        foreach (var tool in AgentTools.Definitions)
        {
            var schema = JsonSerializer.SerializeToElement(tool.InputSchema);
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.GetProperty("properties").ValueKind);
            Assert.NotEmpty(tool.Description);
        }
    }

    [Fact]
    public async Task Vom_Satz_zum_Sieger()
    {
        var anlegen = await Run("create_tournament", new
        {
            name = "Samstagsrunde",
            date = "2026-09-19",
            mode = "RoundRobin",
            bestOf = 1,
            participants = new[] { "Rudi", "Max", "Anna" },
        });

        Assert.False(anlegen.IsError);
        Assert.Equal(AgentTools.WidgetTournament, anlegen.Widget);
        Assert.Contains("Mitschau-Link: https://matchday.test/?t=", anlegen.ResultForModel);
        var id = anlegen.TournamentId!.Value;

        var liste = await Run("list_tournaments", new { });
        Assert.Equal(AgentTools.WidgetList, liste.Widget);
        Assert.Contains("Samstagsrunde", liste.ResultForModel);

        var mehr = await Run("add_participants", new { names = new[] { "Tom" } }, id);
        Assert.Equal(AgentTools.WidgetParticipants, mehr.Widget);
        Assert.Contains("Teilnehmer (4)", mehr.ResultForModel);

        var weniger = await Run("remove_participants", new { names = new[] { "tom", "Niemand" } }, id);
        Assert.Contains("Teilnehmer (3)", weniger.ResultForModel);
        Assert.Contains("Nicht gefunden: Niemand", weniger.ResultForModel);

        var zuFrueh = await Run("record_result", new { winner = "Rudi", loser = "Max", sets = new[] { new[] { 6, 4 } } }, id);
        Assert.True(zuFrueh.IsError);

        var los = await Run("draw", new { }, id);
        Assert.Equal(AgentTools.WidgetStandings, los.Widget);
        Assert.Contains("Runde 1", los.ResultForModel);

        var ergebnis = await Run("record_result", new { winner = "max", loser = "Rudi", sets = new[] { new[] { 7, 6, 4 } } }, id);
        Assert.False(ergebnis.IsError);
        Assert.Contains("→ Max 7:6 (4)", ergebnis.ResultForModel);

        var falsch = await Run("record_result", new { winner = "Anna", loser = "Rudi", sets = new[] { new[] { 6, 6 } } }, id);
        Assert.True(falsch.IsError);
        Assert.Contains("unentschieden", falsch.ResultForModel);

        var kampflos = await Run("record_result", new { winner = "Anna", loser = "Rudi", kind = "walkover" }, id);
        Assert.Contains("→ Anna kampflos", kampflos.ResultForModel);

        var zurueck = await Run("clear_result", new { nameA = "Rudi", nameB = "Anna" }, id);
        Assert.False(zurueck.IsError);
        Assert.Contains("(offen)", zurueck.ResultForModel);
        Assert.DoesNotContain("kampflos", zurueck.ResultForModel);

        var links = await Run("share_links", new { }, id);
        Assert.Equal(AgentTools.WidgetShare, links.Widget);
        Assert.Contains("?a=", links.ResultForModel);

        var ohneKontext = await Run("share_links", new { });
        Assert.True(ohneKontext.IsError);

        var weg = await Run("delete_tournament", new { tournamentId = id.ToString() });
        Assert.False(weg.IsError);
        Assert.Null(weg.TournamentId);
        Assert.Empty(await _a.Actions.ListMineAsync(_a.Rudi));
    }

    [Fact]
    public async Task Ergebnisse_werden_ueber_Namen_gefunden()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna", "Tom"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);

        var hf = gelost.Matches[0];
        var (gefunden, seite) = AgentTools.FindMatch(gelost, gelost.NameOf(hf.Side2), gelost.NameOf(hf.Side1));
        Assert.Equal(hf.Id, gefunden.Id);
        Assert.Equal(2, seite);

        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, "Rudi", "Rudi"));
        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, "Rudi", "Unbekannt"));
    }

    [Fact]
    public async Task Ein_Fremder_bekommt_eine_Absage_statt_einer_Ausnahme()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        var outcome = await _a.Tools.ExecuteAsync(
            "add_participants", In(new { names = new[] { "X" } }), _a.Fremder, t.Id, "https://matchday.test", CancellationToken.None);

        Assert.True(outcome.IsError);
        Assert.Contains("Verwalterlink", outcome.ResultForModel);

        var unbekannt = await Run("kaputt", new { });
        Assert.True(unbekannt.IsError);
    }

    [Fact]
    public async Task Aendern_ueber_das_Werkzeug()
    {
        var anlegen = await Run("create_tournament", new { name = "Cup" });
        var id = anlegen.TournamentId!.Value;

        var geaendert = await Run("update_tournament", new { name = "Herbstcup", date = "2026-10-03", location = "Wien", tiebreakAt = 4 }, id);
        Assert.False(geaendert.IsError);
        Assert.Contains("Herbstcup", geaendert.ResultForModel);
        Assert.Contains("Ort: Wien", geaendert.ResultForModel);
        Assert.Contains("bis 4", geaendert.ResultForModel);

        var entfernt = await Run("update_tournament", new { date = "", location = "" }, id);
        Assert.DoesNotContain("Ort:", entfernt.ResultForModel);

        var kaputt = await Run("update_tournament", new { date = "Samstag" }, id);
        Assert.True(kaputt.IsError);
    }

    [Fact]
    public async Task Ein_Ko_Turnier_vom_Auslosen_bis_zum_Abschluss()
    {
        var anlegen = await Run("create_tournament", new
        {
            name = "Herbstcup",
            mode = "Knockout",
            bestOf = 3,
            finalSet = "Regular",
            participants = new[] { "Rudi", "Max", "Anna", "Tom" },
        });
        var id = anlegen.TournamentId!.Value;
        await Run("draw", new { }, id);

        // Ein K.o.-Turnier zeigt den Baum, und das Finale kennt seine Gegner noch nicht.
        var baum = await Run("get_tournament", new { tournamentId = id.ToString() });
        Assert.Equal(AgentTools.WidgetBracket, baum.Widget);
        Assert.Contains("Platzierung:", baum.ResultForModel);
        Assert.Contains("(Gegner offen)", baum.ResultForModel);
        Assert.Contains("läuft", baum.ResultForModel);

        var gelost = await _a.Actions.GetAsync(id);
        var ersteRunde = gelost.Matches.Where(m => m.Status == MatchStatus.Ready).ToList();
        Assert.Equal(2, ersteRunde.Count);

        // Der Sieger steht auf Seite 2 — das Ergebnis muss trotzdem aus seiner
        // Sicht herauskommen.
        var sieger = gelost.NameOf(ersteRunde[0].Side2);
        var eins = await Run(
            "record_result",
            new { winner = sieger, loser = gelost.NameOf(ersteRunde[0].Side1), sets = new[] { new[] { 6, 3 }, new[] { 6, 4 } } },
            id);
        Assert.Contains($"→ {sieger} 6:3, 6:4", eins.ResultForModel);

        // Eine Aufgabe im zweiten Satz — auch sie steht aus Sicht des Siegers da.
        var zwei = await Run(
            "record_result",
            new
            {
                winner = gelost.NameOf(ersteRunde[1].Side2),
                loser = gelost.NameOf(ersteRunde[1].Side1),
                kind = "retired",
                sets = new[] { new[] { 6, 4 } },
                abandonedSet = new[] { 3, 2 },
            },
            id);
        Assert.Contains("(Aufgabe)", zwei.ResultForModel);

        // Jetzt steht das Finale, und danach ist das Turnier durch.
        var stand = await _a.Actions.GetAsync(id);
        var finale = stand.Matches.Single(m => m.Status == MatchStatus.Ready);
        var ende = await Run(
            "record_result",
            new { winner = stand.NameOf(finale.Side1), loser = stand.NameOf(finale.Side2), sets = new[] { new[] { 6, 0 }, new[] { 6, 1 } } },
            id);
        Assert.Contains("abgeschlossen", ende.ResultForModel);

        var zurueck = await Run("undo_draw", new { }, id);
        Assert.Equal(AgentTools.WidgetParticipants, zurueck.Widget);
        Assert.Contains("Vorbereitung", zurueck.ResultForModel);
    }

    [Fact]
    public async Task Zwei_die_nicht_gegeneinander_spielen()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna", "Tom"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);
        var ersteRunde = gelost.Matches.Where(m => m.Status == MatchStatus.Ready).ToList();

        var fremdePaarung = await Run(
            "record_result",
            new { winner = gelost.NameOf(ersteRunde[0].Side1), loser = gelost.NameOf(ersteRunde[1].Side1), sets = new[] { new[] { 6, 4 } } },
            t.Id);

        Assert.True(fremdePaarung.IsError);
        Assert.Contains("spielen (noch) nicht gegeneinander", fremdePaarung.ResultForModel);

        // Wer gar nicht mitspielt, wird als Erster genauso vermisst wie als Zweiter.
        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, "Unbekannt", "Rudi"));
    }

    [Fact]
    public async Task Jede_Paarung_wird_in_beiden_Richtungen_gefunden()
    {
        // Jeder gegen jeden: sechs Matches, und jedes muss sich über seine zwei
        // Namen finden lassen — egal, welcher zuerst genannt wird.
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup", Mode: Mode.RoundRobin));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna", "Tom"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);

        Assert.Equal(6, gelost.Matches.Count);

        foreach (var match in gelost.Matches)
        {
            var erster = gelost.NameOf(match.Side1);
            var zweiter = gelost.NameOf(match.Side2);

            var (vorwaerts, seiteVorne) = AgentTools.FindMatch(gelost, erster, zweiter);
            Assert.Equal(match.Id, vorwaerts.Id);
            Assert.Equal(1, seiteVorne);

            var (rueckwaerts, seiteHinten) = AgentTools.FindMatch(gelost, zweiter, erster);
            Assert.Equal(match.Id, rueckwaerts.Id);
            Assert.Equal(2, seiteHinten);
        }
    }

    [Fact]
    public async Task Wer_ein_Freilos_hat_spielt_gegen_niemanden()
    {
        // Drei Teilnehmer: einer kommt ohne Gegner in die nächste Runde. Sucht das
        // Modell eine Paarung mit ihm, darf die leere Seite nicht mitzählen.
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));
        await _a.Actions.AddParticipantsAsync(_a.Rudi, t.Id, ["Rudi", "Max", "Anna"]);
        var gelost = await _a.Actions.DrawAsync(_a.Rudi, t.Id);

        var freilos = gelost.Matches.Single(m => m.IsBye);
        var durch = gelost.NameOf(freilos.Side1);
        var anderer = gelost.Participants.First(p => p.Name != durch).Name;

        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, durch, anderer));
        Assert.Throws<DomainException>(() => AgentTools.FindMatch(gelost, anderer, durch));
    }

    [Fact]
    public async Task Ein_Turnier_in_Vorbereitung_zeigt_seinen_Rahmen()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));

        var gezeigt = await Run("get_tournament", new { tournamentId = t.Id.ToString() });

        Assert.Equal(AgentTools.WidgetTournament, gezeigt.Widget);
        Assert.Contains("Vorbereitung (noch nicht ausgelost)", gezeigt.ResultForModel);
    }

    [Fact]
    public async Task Was_das_Modell_falsch_ausfuellen_kann()
    {
        // Kein Name: das Turnier braucht einen.
        Assert.True((await Run("create_tournament", new { })).IsError);

        // Ein Modus, den es nicht gibt.
        Assert.True((await Run("create_tournament", new { name = "Cup", mode = "Rundlauf" })).IsError);

        // Eine Id, die keine ist.
        var keineId = await Run("get_tournament", new { tournamentId = "irgendwas" });
        Assert.True(keineId.IsError);
        Assert.Contains("ist keine Turnier-Id", keineId.ResultForModel);

        var anlegen = await Run("create_tournament", new { name = "Cup", bestOf = 3, finalSet = "Regular", participants = new[] { "Rudi", "Max" } });
        var id = anlegen.TournamentId!.Value;
        await Run("draw", new { }, id);

        // Ein Satz aus einer Zahl ist kein Satz.
        var halberSatz = await Run("record_result", new { winner = "Rudi", loser = "Max", sets = new[] { new[] { 6 } } }, id);
        Assert.True(halberSatz.IsError);
        Assert.Contains("zwei Zahlen", halberSatz.ResultForModel);

        // Ohne Namen findet das Werkzeug niemanden.
        Assert.True((await Run("record_result", new { sets = new[] { new[] { 6, 4 } } }, id)).IsError);
        Assert.True((await Run("clear_result", new { }, id)).IsError);

        // Ein aufgegebener Satz, der keine Liste ist, wird einfach nicht gelesen.
        var aufgabe = await Run("record_result", new { winner = "Rudi", loser = "Max", kind = "retired", sets = new[] { new[] { 6, 4 } }, abandonedSet = "vergessen" }, id);
        Assert.False(aufgabe.IsError);
    }

    [Fact]
    public async Task Die_Links_bleiben_der_Turnierleitung()
    {
        var t = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Cup"));

        var fremd = await _a.Tools.ExecuteAsync(
            "share_links", In(new { }), _a.Fremder, t.Id, "https://matchday.test", CancellationToken.None);

        Assert.True(fremd.IsError);
        Assert.Contains("Verwalterlink", fremd.ResultForModel);
    }

    [Fact]
    public async Task Das_Format_laesst_sich_stueckweise_aendern()
    {
        var anlegen = await Run("create_tournament", new { name = "Cup" });
        var id = anlegen.TournamentId!.Value;

        var satzzahl = await Run("update_tournament", new { bestOf = 5 }, id);
        Assert.Contains("3 Gewinnsätze", satzzahl.ResultForModel);

        var letzterSatz = await Run("update_tournament", new { finalSet = "Advantage" }, id);
        Assert.Contains("letzter Satz ohne Tiebreak", letzterSatz.ResultForModel);

        // Ohne Formatangabe bleibt das Format, wie es war.
        var nurName = await Run("update_tournament", new { name = "Wintercup" }, id);
        Assert.Contains("Wintercup", nurName.ResultForModel);
        Assert.Contains("3 Gewinnsätze", nurName.ResultForModel);
        Assert.Contains("letzter Satz ohne Tiebreak", nurName.ResultForModel);
    }

    [Fact]
    public async Task Alle_gestrichenen_Namen_gefunden()
    {
        var anlegen = await Run("create_tournament", new { name = "Cup", participants = new[] { "Rudi", "Max", "Anna" } });
        var id = anlegen.TournamentId!.Value;

        var weg = await Run("remove_participants", new { names = new[] { "Rudi", "Anna" } }, id);

        Assert.False(weg.IsError);
        Assert.Contains("Teilnehmer (1): Max", weg.ResultForModel);
        Assert.DoesNotContain("Nicht gefunden", weg.ResultForModel);
    }
}
