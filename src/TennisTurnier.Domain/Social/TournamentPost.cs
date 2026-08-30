using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Social;

/// <summary>
/// Was für ein Eintrag das ist.
///
/// Die Unterscheidung liegt nicht am Text — der steht bei allen fertig da
/// (ADR-0014) —, sondern an dem, was die Oberfläche damit tut: ein Beitrag
/// bekommt einen Verfasser und einen Knopf zum Zurücknehmen, ein Ereignis ein
/// Symbol und einen Verweis auf das, worüber es berichtet.
/// </summary>
public enum PostKind
{
    /// <summary>Von einem Mitglied geschrieben. Der einzige Wert mit Verfasser.</summary>
    Message,

    /// <summary>Jemand ist der Gruppe beigetreten.</summary>
    Joined,

    /// <summary>Der Draw steht.</summary>
    DrawGenerated,

    /// <summary>Ein Ergebnis ist eingetragen.</summary>
    ResultRecorded,

    /// <summary>Der Spielplan ist bestätigt.</summary>
    ScheduleConfirmed,

    /// <summary>Das Turnier hat seinen Zustand gewechselt — Meldung offen, gestartet, beendet.</summary>
    StateChanged,
}

/// <summary>
/// Ein Eintrag im Feed eines Turniers.
///
/// Er trägt seinen Text fertig, auch als Ereignis. Ein Eintrag, der zur
/// Anzeigezeit aus dem Match gerendert würde, wäre normalisiert und immer
/// aktuell — und genau deshalb falsch: ein Feed ist ein Protokoll. Wird ein
/// Ergebnis später korrigiert, bleibt die alte Zeile stehen, und darunter kommt
/// eine neue (ADR-0014).
/// </summary>
public sealed class TournamentPost : Entity
{
    /// <summary>
    /// Deckt sich mit der Spalte in der Persistenz. Sie steht hier, weil SQLite
    /// Längenangaben nicht durchsetzt: ein zu langer Text ginge dort still
    /// durch und fiele erst auf einer Datenbank auf, die es genauer nimmt
    /// (ADR-0006).
    /// </summary>
    public const int MaxTextLength = 2000;

    private readonly List<PostComment> _comments = [];

    private TournamentPost(
        Guid id,
        Guid tournamentId,
        PostKind kind,
        Guid? authorUserId,
        string text,
        Guid? matchId,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (tournamentId == Guid.Empty)
        {
            throw new DomainException("Ein Feed-Eintrag braucht ein Turnier.");
        }

        TournamentId = tournamentId;
        Kind = kind;
        AuthorUserId = authorUserId;
        Text = Validate(text);
        MatchId = matchId;
        CreatedAt = createdAt;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private TournamentPost(Guid id) : base(id) => Text = string.Empty;

    public Guid TournamentId { get; private set; }

    public PostKind Kind { get; private set; }

    /// <summary>
    /// Wer geschrieben hat — leer bei einem Ereignis.
    ///
    /// Ein Ereignis dem Schiedsrichter zuzuschreiben, der das Ergebnis
    /// eintippt, wäre eine Behauptung über eine Handlung, die dem Turnier
    /// gehört und nicht ihm.
    /// </summary>
    public Guid? AuthorUserId { get; private set; }

    public string Text { get; private set; }

    /// <summary>
    /// Das Match, über das der Eintrag berichtet — damit die Oberfläche
    /// dorthin verweisen kann. Leer bei allem, was kein Match betrifft.
    /// </summary>
    public Guid? MatchId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PostComment> Comments => _comments;

    /// <summary>Ein Beitrag eines Mitglieds.</summary>
    public static TournamentPost Message(
        Guid id,
        Guid tournamentId,
        Guid authorUserId,
        string text,
        DateTimeOffset createdAt)
    {
        if (authorUserId == Guid.Empty)
        {
            throw new DomainException("Ein Beitrag braucht einen Verfasser.");
        }

        return new TournamentPost(id, tournamentId, PostKind.Message, authorUserId, text, null, createdAt);
    }

    /// <summary>
    /// Ein Ereignis. Ohne Verfasser und ohne Weg, es später zu ändern — es ist
    /// die Chronik, und wer sie ändern darf, hat keine.
    /// </summary>
    public static TournamentPost Event(
        Guid id,
        Guid tournamentId,
        PostKind kind,
        string text,
        DateTimeOffset createdAt,
        Guid? matchId = null)
    {
        if (kind == PostKind.Message)
        {
            throw new DomainException("Ein Ereignis ist kein Beitrag — ihm fehlt der Verfasser.");
        }

        return new TournamentPost(id, tournamentId, kind, null, text, matchId, createdAt);
    }

    /// <summary>Ist das ein geschriebener Beitrag und kein Ereignis?</summary>
    public bool IsMessage => Kind == PostKind.Message;

    public PostComment Comment(Guid id, Guid authorUserId, string text, DateTimeOffset createdAt)
    {
        var comment = new PostComment(id, Id, authorUserId, text, createdAt);
        _comments.Add(comment);

        return comment;
    }

    /// <summary>
    /// Nimmt einen Kommentar zurück. Ein unbekannter ist kein Fehler: derselbe
    /// Klick zweimal ist derselbe Wille.
    /// </summary>
    public void RemoveComment(Guid commentId) =>
        _comments.RemoveAll(comment => comment.Id == commentId);

    internal static string Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainException("Ein leerer Beitrag ist keiner.");
        }

        var trimmed = text.Trim();

        return trimmed.Length <= MaxTextLength
            ? trimmed
            : throw new DomainException(
                $"Ein Beitrag darf höchstens {MaxTextLength} Zeichen haben, war {trimmed.Length}.");
    }

    public override string ToString() => $"{Kind}: {Text}";
}

/// <summary>
/// Eine Antwort unter einem Eintrag.
///
/// Flach und nicht verschachtelt: ein Kommentar auf einen Kommentar verlangt
/// eine Einrückung, die auf einem Telefon nach drei Ebenen keine Breite mehr
/// hat — und in einer Vereinsgruppe redet ohnehin niemand in Bäumen.
/// </summary>
public sealed class PostComment : Entity
{
    internal PostComment(Guid id, Guid postId, Guid authorUserId, string text, DateTimeOffset createdAt)
        : base(id)
    {
        if (authorUserId == Guid.Empty)
        {
            throw new DomainException("Ein Kommentar braucht einen Verfasser.");
        }

        PostId = postId;
        AuthorUserId = authorUserId;
        Text = TournamentPost.Validate(text);
        CreatedAt = createdAt;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private PostComment(Guid id) : base(id) => Text = string.Empty;

    public Guid PostId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public override string ToString() => Text;
}
