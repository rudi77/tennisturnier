using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Phases;

namespace TennisTurnier.Application.Tournaments;

/// <summary>
/// Woher eine Match-Seite kommt, in Worten.
///
/// „Sieger aus Halbfinale 1" statt „Sieger aus 8ce36d9f-300e-43e2…". Ein Bracket
/// vor dem ersten Ball lesbar zu machen ist der Sinn der Konstruktion aus
/// ADR-0001; eine Kennung an dieser Stelle ist technisch richtig und für den,
/// der davorsteht, wertlos.
///
/// Gemeinsam genutzt von der öffentlichen Projektion und der Ansicht der
/// Turnierleitung. Getrennte Fassungen hatten genau den Fehler zur Folge, der
/// diese Klasse veranlasst hat: die öffentliche Ansicht war lesbar, die interne
/// zeigte Kennungen — obwohl der Vertrag für beide dasselbe zusagt.
/// </summary>
public static class MatchOrigins
{
    /// <summary>
    /// Ein sprechender Name je Match.
    ///
    /// Tragen mehrere Matches dasselbe Etikett — zwei Halbfinale —, werden sie
    /// nach ihrer Position durchnummeriert. Ohne Etikett bleibt die Angabe aus
    /// Runde und Position, was in einer Gruppenphase die einzig mögliche ist.
    /// </summary>
    public static Dictionary<Guid, string> LabelsOf(IReadOnlyList<Match> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

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
