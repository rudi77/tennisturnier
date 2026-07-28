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

public sealed class ScoreComparer : ValueComparer<Score>
{
    public ScoreComparer()
        : base(
            (left, right) => left == right,
            score => FormatJson.Serialize(StoredScore.From(score)).GetHashCode(StringComparison.Ordinal),
            score => FormatJson.Deserialize<StoredScore>(FormatJson.Serialize(StoredScore.From(score)), "Ergebnis")
                .ToScore())
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
