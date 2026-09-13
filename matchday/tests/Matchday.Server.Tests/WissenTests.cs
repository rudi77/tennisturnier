using Matchday.Domain;
using Matchday.Server.Agent;

namespace Matchday.Server.Tests;

/// <summary>
/// Der Agent soll nicht nur handeln, sondern auch erklären — die Anwendung, den
/// Ablauf, die Modi, die Spielregeln. Geprüft wird nicht die Formulierung,
/// sondern dass zu jedem Begriff, den die Domäne anbietet, etwas im Wissen
/// steht: Kommt ein Modus, eine Disziplin oder ein Satzformat dazu, fällt
/// dieser Test um und erinnert daran, es auch zu erklären.
/// </summary>
public sealed class WissenTests
{
    private static readonly Dictionary<Mode, string> Modi = new()
    {
        [Mode.Knockout] = "K.o.",
        [Mode.RoundRobin] = "Jeder gegen jeden",
    };

    private static readonly Dictionary<Discipline, string> Disziplinen = new()
    {
        [Discipline.Singles] = "Einzel",
        [Discipline.Doubles] = "Doppel",
    };

    private static readonly Dictionary<FinalSetMode, string> LetzterSatz = new()
    {
        [FinalSetMode.Regular] = "normal",
        [FinalSetMode.MatchTiebreak10] = "Match-Tiebreak bis 10",
        [FinalSetMode.Advantage] = "ohne Tiebreak",
    };

    [Fact]
    public void Jeder_Modus_wird_erklaert() => Erklaert(Enum.GetValues<Mode>(), Modi);

    [Fact]
    public void Jede_Disziplin_wird_erklaert() => Erklaert(Enum.GetValues<Discipline>(), Disziplinen);

    [Fact]
    public void Jeder_letzte_Satz_wird_erklaert() => Erklaert(Enum.GetValues<FinalSetMode>(), LetzterSatz);

    [Fact]
    public void Das_Wissen_kennt_die_Regeln_nach_denen_gezaehlt_wird()
    {
        foreach (var begriff in new[] { "Einstand", "Tiebreak", "Freilos", "Aufgabe", "Nicht angetreten", "Kreisverfahren" })
        {
            Assert.Contains(begriff, Knowledge.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Das_Wissen_kennt_beide_Wege()
    {
        // Ein Turnier geht im Gespräch, über die Widgets oder gemischt. Wer
        // danach fragt, soll keine halbe Antwort bekommen.
        Assert.Contains("Widgets", Knowledge.Text);
        Assert.Contains("gemischt", Knowledge.Text);
        Assert.Contains("Verwalterlink", Knowledge.Text);
        Assert.Contains("Mitschau-Link", Knowledge.Text);
    }

    [Fact]
    public void Die_Anweisungen_tragen_das_Wissen_und_erlauben_die_Antwort_ohne_Werkzeug()
    {
        Assert.Contains(Knowledge.Text, TournamentAgent.SystemPrompt);
        Assert.Contains("ohne ein Werkzeug zu rufen", TournamentAgent.SystemPrompt);
    }

    [Fact]
    public void Die_Anweisungen_schicken_zufaellige_Teams_an_das_Werkzeug()
    {
        // Der Fall aus dem Gespräch: Auf „mach daraus Teams“ hat der Agent
        // geantwortet, er dürfe das nicht. Er darf — die Anwendung würfelt.
        Assert.Contains("add_random_teams", TournamentAgent.SystemPrompt);
        Assert.Contains("zufällige Teams", TournamentAgent.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("add_random_teams", Knowledge.Text);
    }

    private static void Erklaert<T>(IEnumerable<T> werte, Dictionary<T, string> begriffe)
        where T : notnull
    {
        foreach (var wert in werte)
        {
            Assert.True(begriffe.TryGetValue(wert, out var begriff), $"Für {wert} steht hier kein Begriff — gehört er ins Wissen des Agenten?");
            Assert.Contains(begriff, Knowledge.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
