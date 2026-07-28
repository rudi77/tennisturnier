using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Web;

/// <summary>
/// Kopf und Reiter eines Turniers.
///
/// Ein eigener Typ statt vier ViewData-Einträgen, damit die vier Turnierseiten
/// dieselbe Kopfzeile nicht viermal leicht verschieden bauen.
/// </summary>
public sealed record TournamentNav(TournamentDetail Tournament, string Active)
{
    /// <summary>Die Reiter in der Reihenfolge, in der ein Turnier sie durchläuft.</summary>
    public static IReadOnlyList<(string Label, string Suffix)> Tabs { get; } =
    [
        ("Meldungen", ""),
        ("Bracket", "/draw"),
        ("Spielplan", "/schedule"),
        ("Turniertag", "/matchday"),
    ];
}
