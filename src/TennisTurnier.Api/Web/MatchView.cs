using TennisTurnier.Api.Pages.Tournaments;
using TennisTurnier.Application.Tournaments;

namespace TennisTurnier.Api.Web;

/// <summary>
/// Ein Match samt der Seite, auf der es steht.
///
/// Die Teilansicht braucht beides: das Match für die Anzeige und die Turnier-Id
/// für die Formulare. Ein Tupel täte es auch, aber ein benannter Typ macht die
/// Aufrufstelle lesbar.
/// </summary>
public sealed record MatchView(DrawModel Page, MatchDetail Match);
