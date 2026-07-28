using TennisTurnier.Domain.Matches;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Sprechende Namen für Matches und ihre Herkunftsangaben.
///
/// Ohne sie steht in jeder Ansicht „Sieger aus 8f3c…" — technisch richtig und für
/// jeden Leser wertlos. Das Bracket vor dem ersten Ball lesbar zu machen ist der
/// Sinn der Referenzen aus ADR-0001, und die Zusage steht ausdrücklich im
/// Vertrag von <see cref="MatchSideDetail"/> und <see cref="PublicView.PublicSideView"/>.
///
/// Die Übersetzung liegt hier und nicht in den drei Lesepfaden, weil sie sonst
/// dreimal leicht verschieden ausfällt: die Turnierleitung läse „Halbfinale 1",
/// der Aushang „Halbfinale" und das Platzbrett die Id.
/// </summary>
internal static class MatchLabels
{
    /// <summary>
    /// Ein Name je Match innerhalb einer Phase.
    ///
    /// Tragen mehrere Matches dasselbe Etikett — vier Viertelfinals —, wird
    /// durchnummeriert. Ohne Etikett bleibt die Angabe aus Runde und Position,
    /// die immer eindeutig ist.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> For(IReadOnlyList<Match> matches)
    {
        var byLabel = matches
            .Where(m => m.Label is not null)
            .GroupBy(m => m.Label!)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).ToList());

        return matches.ToDictionary(
            match => match.Id,
            match => match.Label is { } label
                ? byLabel[label].Count > 1
                    ? $"{label} {byLabel[label].IndexOf(match) + 1}"
                    : label
                : $"Runde {match.Round}, Match {match.Position}");
    }

    /// <summary>
    /// Die Herkunft einer Match-Seite im Klartext.
    ///
    /// Ein Vorspiel aus einer anderen Phase steht nicht in <paramref name="labels"/>
    /// — dann bleibt es bei „einem Vorspiel". Das ist die ehrlichere Auskunft als
    /// eine Id, die ohnehin niemand nachschlägt.
    /// </summary>
    public static string Describe(ParticipantRef origin, IReadOnlyDictionary<Guid, string> labels) => origin switch
    {
        ParticipantRef.Entry => "gesetzt",
        ParticipantRef.WinnerOf winner => $"Sieger aus {labels.GetValueOrDefault(winner.MatchId, "einem Vorspiel")}",
        ParticipantRef.LoserOf loser => $"Verlierer aus {labels.GetValueOrDefault(loser.MatchId, "einem Vorspiel")}",
        ParticipantRef.GroupPosition group => group.ToString(),
        ParticipantRef.Bye => "Freilos",
        _ => "offen",
    };
}
