namespace Matchday.Domain;

/// <summary>Was gespielt wird.</summary>
public enum Discipline
{
    /// <summary>Einzel: ein Spieler je Seite.</summary>
    Singles,

    /// <summary>Doppel: ein Paar je Seite. Ein Teilnehmer ist dann ein Team aus zwei Spielern.</summary>
    Doubles,

    /// <summary>
    /// Noch offen: Die Namen stehen schon, entschieden wird später — spätestens
    /// vor der Auslosung (ADR-0027). Steht bewusst hinten: Ein Turnier ohne
    /// Angabe liest sich weiter als Einzel.
    /// </summary>
    Open,
}

public static class Disciplines
{
    /// <summary>So steht die Disziplin in Sätzen, Kopfzeilen und Vorschauen.</summary>
    public static string Describe(this Discipline discipline) => discipline switch
    {
        Discipline.Singles => "Einzel",
        Discipline.Doubles => "Doppel",
        _ => "Einzel oder Doppel offen",
    };

    /// <summary>Wie die Einträge heißen: im Doppel Teams, sonst Teilnehmer.</summary>
    public static string Entries(this Discipline discipline) => discipline == Discipline.Doubles ? "Teams" : "Teilnehmer";
}

/// <summary>
/// Ein Teilnehmer ist ein Name. Im Doppel ist das der Name des Paares, und die
/// beiden Spieler stehen in <see cref="Players"/>.
///
/// Das Feld ist freiwillig, damit Turniere aus der Zeit vor dem Doppel ohne
/// Wanderung weiterlesen: Fehlt es, ist der Name der einzige Spieler.
/// </summary>
public sealed record Participant(Guid Id, string Name, IReadOnlyList<string>? Players = null)
{
    /// <summary>Die Spieler: im Einzel einer, im Doppel zwei.</summary>
    public IReadOnlyList<string> Lineup => Players is { Count: > 0 } ? Players : [Name];

    /// <summary>Heißt der Teilnehmer so — als Paar oder als einer seiner Spieler?</summary>
    public bool IsCalled(string text) =>
        Same(Name, text) || Lineup.Any(player => Same(player, text));

    /// <summary>Dieselbe Aufstellung, auf die Reihenfolge kommt es nicht an.</summary>
    public bool Is(IReadOnlyList<string> players) =>
        players.Count == Lineup.Count && players.All(asked => Lineup.Any(own => Same(own, asked)));

    /// <summary>Steckt der Text im Namen oder in einem der Spieler?</summary>
    public bool Mentions(string text) =>
        Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Lineup.Any(player => player.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Spielt dieser Spieler hier mit?</summary>
    public bool Has(string player) => Lineup.Any(own => Same(own, player));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Eine Aufstellung ist, was in ein Namensfeld getippt wird: im Einzel ein
/// Name, im Doppel zwei — getrennt durch „/“, „&amp;“, „+“ oder „und“. Das
/// Komma bleibt frei: Damit trennt die Oberfläche mehrere Teilnehmer.
/// </summary>
public static class Lineups
{
    /// <summary>So steht ein Doppel überall, wo es geschrieben wird.</summary>
    public const string Separator = " / ";

    private static readonly string[] Marks = ["/", "&", "+", " und ", " mit ", " u. "];

    /// <summary>Zerlegt einen Eintrag in Spieler. Leere Teile fallen weg.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var parts = new List<string> { text };

        foreach (var mark in Marks)
        {
            parts = parts
                .SelectMany(part => part.Split(mark, StringSplitOptions.TrimEntries))
                .ToList();
        }

        return parts.Where(part => part.Length > 0).ToList();
    }

    /// <summary>Der Name, unter dem eine Aufstellung überall auftaucht.</summary>
    public static string Compose(IReadOnlyList<string> players) => string.Join(Separator, players);
}
