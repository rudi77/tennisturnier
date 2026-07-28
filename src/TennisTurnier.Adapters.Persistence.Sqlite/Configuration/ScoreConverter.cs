using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Adapters.Persistence.Sqlite.Configuration;

/// <summary>
/// Legt das Ergebnis als eine JSON-Spalte ab. Es wird immer als Ganzes gelesen
/// und nie serverseitig durchsucht (ADR-0006).
/// </summary>
public sealed class ScoreConverter : ValueConverter<Score, string>
{
    public ScoreConverter()
        : base(
            score => FormatJson.Serialize(StoredScore.From(score)),
            json => FormatJson.Deserialize<StoredScore>(json, "Ergebnis").ToScore())
    {
    }
}

/// <summary>
/// Vergleicht Ergebnisse nach Inhalt.
///
/// Die Momentaufnahme ist das Ergebnis selbst: <see cref="Score"/> ist
/// unveränderlich, eine Kopie könnte sich also nie von ihm unterscheiden. Zuvor
/// entstand sie über Serialisieren und Zurücklesen — und weil der Vergleich
/// damit auf zwei verschiedene Objekte traf, hielt die Änderungsverfolgung jedes
/// geladene Match für verändert und schrieb es bei jedem Speichern zurück. Auf
/// SQLite, das datenbankweit serialisiert, ist das spürbar.
///
/// Die inhaltliche Gleichheit bringt <see cref="Score"/> selbst mit, deshalb
/// genügt hier der Operator. Sie wird gebraucht, sobald ein inhaltsgleiches
/// Ergebnis neu zugewiesen wird.
/// </summary>
public sealed class ScoreComparer : ValueComparer<Score>
{
    public ScoreComparer()
        : base(
            (left, right) => left == right,
            score => score.GetHashCode(),
            score => score)
    {
    }
}

/// <summary>
/// Die Ablageform des Ergebnisses.
///
/// <see cref="Score"/> hat bewusst nur benannte Fabrikmethoden und keinen
/// öffentlichen Konstruktor — genau das macht ungültige Ergebnisse unmöglich.
/// Diese Zwischenform hält die rohen Werte und reicht sie beim Lesen an
/// <see cref="Score.Rehydrate"/> weiter, das auf eine erneute Prüfung
/// verzichtet: die Regeln des Turniers sind hier nicht zur Hand, und sie zu
/// erraten würde gültige Ergebnisse unlesbar machen.
/// </summary>
internal sealed record StoredScore(
    MatchOutcome Outcome,
    int WinnerSide,
    IReadOnlyList<SetScore> CompletedSets,
    SetScore? AbandonedSet)
{
    public static StoredScore From(Score score) =>
        new(score.Outcome, score.WinnerSide, score.CompletedSets, score.AbandonedSet);

    public Score ToScore() => Score.Rehydrate(Outcome, WinnerSide, CompletedSets, AbandonedSet);
}
