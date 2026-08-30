using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Social;

/// <summary>
/// Ein Eintrag im Feed, wie ihn die Oberfläche bekommt.
/// </summary>
/// <param name="Author">
/// Leer bei einem Ereignis. Ein Ereignis gehört dem Turnier und nicht dem, der
/// gerade das Ergebnis eingetippt hat (ADR-0014).
/// </param>
/// <param name="MatchId">
/// Das Match, über das der Eintrag berichtet — damit die Oberfläche dorthin
/// verweisen kann.
/// </param>
/// <param name="CanDelete">
/// Darf der Aufrufer diesen Eintrag zurücknehmen? Der Verfasser darf seinen
/// eigenen, die Turnierleitung jeden — ein Ereignis niemand. Die Antwort sagt,
/// was die Oberfläche anbietet; ob es erlaubt ist, entscheidet weiterhin der
/// Anwendungsfall.
/// </param>
public sealed record FeedPostView(
    Guid Id,
    PostKind Kind,
    FeedAuthorView? Author,
    string Text,
    Guid? MatchId,
    DateTimeOffset CreatedAt,
    bool CanDelete,
    IReadOnlyList<FeedCommentView> Comments);

public sealed record FeedCommentView(
    Guid Id,
    FeedAuthorView Author,
    string Text,
    DateTimeOffset CreatedAt,
    bool CanDelete);

/// <summary>
/// Wer geschrieben hat.
///
/// Der Name wird beim Lesen nachgeschlagen und nicht im Eintrag festgehalten:
/// anders als der Anzeigename eines Teilnehmers, der zu einem Turnier gehört
/// und dort eingefroren wird, ist das hier die Person selbst — wer umbenannt
/// wird, heißt auch unter seinen alten Beiträgen so.
/// </summary>
/// <param name="PlayerId">
/// Der Spieler zu diesem Konto, sofern es einen gibt. Er ist der Weg vom
/// Beitrag ins Profil (ADR-0013); ohne Spieler bleibt der Name ein Name.
/// </param>
public sealed record FeedAuthorView(Guid UserId, string DisplayName, Guid? PlayerId);

/// <summary>
/// Eine Seite des Feeds.
/// </summary>
/// <param name="Before">
/// Der Zeitstempel, mit dem die nächste Seite geholt wird — leer, wenn es keine
/// mehr gibt. Ein Zeitstempel und keine Seitennummer: ein neuer Eintrag
/// verschiebt sonst jede Grenze dahinter, und man liest denselben Beitrag
/// zweimal.
/// </param>
/// <param name="CanWrite">
/// Darf der Aufrufer hier schreiben? Sonst zeigt die Oberfläche kein Feld.
/// </param>
public sealed record FeedPage(
    IReadOnlyList<FeedPostView> Posts,
    DateTimeOffset? Before,
    bool CanWrite);

public sealed record WritePostRequest(string Text);
