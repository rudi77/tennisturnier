using TennisTurnier.Application.Common;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;
using TennisTurnier.Domain.Social;

namespace TennisTurnier.Application.Social;

public interface IFeedService
{
    Task<FeedPage> ListAsync(
        Guid tournamentId,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    Task<FeedPostView> PostAsync(
        Guid tournamentId,
        WritePostRequest request,
        CancellationToken cancellationToken = default);

    Task<FeedCommentView> CommentAsync(
        Guid postId,
        WritePostRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nimmt einen Beitrag zurück. Ein Ereignis lässt sich nicht löschen — es
    /// ist die Chronik, und wer sie ändern darf, hat keine (ADR-0014).
    /// </summary>
    Task DeletePostAsync(Guid postId, CancellationToken cancellationToken = default);

    Task DeleteCommentAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Der Feed eines Turniers, von der Seite der Mitglieder aus (ADR-0014).
///
/// Zwei Grenzen, und sie tun Verschiedenes. Der Query-Filter entscheidet, wer
/// den Feed <em>sieht</em> — er hängt am Turnier wie alles andere und braucht
/// hier keine Zeile. Die Rechtematrix entscheidet, wer <em>schreibt</em>: das
/// ist `WriteInFeed`, und es haben Mitglied, Schiedsrichter und Turnierleitung.
///
/// Gelöscht wird enger: der Verfasser darf seinen eigenen Beitrag zurücknehmen,
/// die Turnierleitung jeden. Das ist Moderation und in einer Vereinsgruppe
/// gelegentlich nötig.
/// </summary>
public sealed class FeedService : IFeedService
{
    /// <summary>
    /// So viele Einträge gehen höchstens in einem Zug hinaus. Eine Grenze und
    /// keine Vorgabe: der Aufrufer darf weniger verlangen, aber nicht mehr —
    /// sonst zieht ein Turnier mit zweitausend Einträgen die ganze Tabelle.
    /// </summary>
    private const int MaxPageSize = 100;

    private readonly IFeedRepository _feed;
    private readonly ITournamentRepository _tournaments;
    private readonly IPlayerHistoryStore _players;
    private readonly IUserDirectory _directory;
    private readonly IPostCommitQueue _postCommit;
    private readonly ITournamentNotifier _notifier;
    private readonly IUserContext _userContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public FeedService(
        IFeedRepository feed,
        ITournamentRepository tournaments,
        IPlayerHistoryStore players,
        IUserDirectory directory,
        IPostCommitQueue postCommit,
        ITournamentNotifier notifier,
        IUserContext userContext,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _feed = feed;
        _tournaments = tournaments;
        _players = players;
        _directory = directory;
        _postCommit = postCommit;
        _notifier = notifier;
        _userContext = userContext;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<FeedPage> ListAsync(
        Guid tournamentId,
        int limit = 50,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        // Über das Repository und nicht über die Zählung der Einträge: ein
        // Turnier ohne einen einzigen Beitrag ist nicht dasselbe wie ein
        // Turnier, das der Aufrufer nicht sehen darf. Ohne diesen Griff wären
        // beide eine leere Liste.
        await RequireVisibleAsync(tournamentId, cancellationToken);

        var size = Math.Clamp(limit, 1, MaxPageSize);
        var posts = await _feed.ListAsync(tournamentId, size, before, cancellationToken);
        var authors = await AuthorsAsync(posts, cancellationToken);

        return new FeedPage(
            [.. posts.Select(post => ToView(post, authors, tournamentId))],
            // Eine volle Seite heißt: es könnte mehr geben. Das ist absichtlich
            // eine Vermutung und keine zweite Abfrage — sie kostete bei jedem
            // Aufruf eine Zählung über die ganze Tabelle, um am Ende eine Seite
            // zu sparen, die leer zurückkommt.
            posts.Count == size ? posts[^1].CreatedAt : null,
            MayWrite(tournamentId));
    }

    public async Task<FeedPostView> PostAsync(
        Guid tournamentId,
        WritePostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await RequireVisibleAsync(tournamentId, cancellationToken);
        _userContext.Current.Require(Permission.WriteInFeed, ResourceScope.Tournament(tournamentId));

        var post = TournamentPost.Message(
            NextId(), tournamentId, _userContext.Current.UserId, request.Text, _clock.Now);

        _feed.Add(post);
        Announce(tournamentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var authors = await AuthorsAsync([post], cancellationToken);

        return ToView(post, authors, tournamentId);
    }

    public async Task<FeedCommentView> CommentAsync(
        Guid postId,
        WritePostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var post = await LoadAsync(postId, cancellationToken);
        _userContext.Current.Require(Permission.WriteInFeed, ResourceScope.Tournament(post.TournamentId));

        var comment = post.Comment(
            NextId(), _userContext.Current.UserId, request.Text, _clock.Now);

        Announce(post.TournamentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var authors = await AuthorsAsync([post], cancellationToken);

        return ToComment(comment, authors, post.TournamentId);
    }

    public async Task DeletePostAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var post = await LoadAsync(postId, cancellationToken);

        if (!post.IsMessage)
        {
            throw new Domain.Common.DomainException(
                "Ein Ereignis gehört zur Chronik des Turniers und lässt sich nicht zurücknehmen.");
        }

        RequireMayDelete(post.TournamentId, post.AuthorUserId);

        _feed.Remove(post);
        Announce(post.TournamentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCommentAsync(
        Guid postId,
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        var post = await LoadAsync(postId, cancellationToken);

        var comment = post.Comments.FirstOrDefault(c => c.Id == commentId)
            ?? throw new NotFoundException("Kommentar", commentId);

        RequireMayDelete(post.TournamentId, comment.AuthorUserId);

        post.RemoveComment(commentId);
        Announce(post.TournamentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sein eigenes Wort nimmt jeder zurück; fremdes nur die Turnierleitung.
    /// Beides endet in einem 404 und nicht in einem 403 — auch hier soll die
    /// Antwort nicht verraten, was es zu sehen gäbe (ADR-0004).
    /// </summary>
    private void RequireMayDelete(Guid tournamentId, Guid? authorUserId)
    {
        var user = _userContext.Current;

        if (authorUserId is { } author && author == user.UserId && user.IsAuthenticated)
        {
            return;
        }

        user.Require(Permission.ManageTournament, ResourceScope.Tournament(tournamentId));
    }

    private bool MayWrite(Guid tournamentId) =>
        _userContext.Current.Can(Permission.WriteInFeed, ResourceScope.Tournament(tournamentId));

    /// <summary>
    /// Der Eintrag samt seinem Turnier — beide durch den Query-Filter, und ein
    /// unsichtbares Turnier lässt den Eintrag gar nicht erst entstehen.
    /// </summary>
    private async Task<TournamentPost> LoadAsync(Guid postId, CancellationToken cancellationToken) =>
        await _feed.FindAsync(postId, cancellationToken)
        ?? throw new NotFoundException("Feed-Eintrag", postId);

    private async Task RequireVisibleAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        if (await _tournaments.FindAsync(tournamentId, cancellationToken) is null)
        {
            throw new NotFoundException("Turnier", tournamentId);
        }
    }

    /// <summary>
    /// Die Verfasser in einem Zug: Konten für die Namen, Spieler für den Weg
    /// ins Profil. Je Eintrag nachzuschlagen wären bei fünfzig Einträgen
    /// hundert Abfragen.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, FeedAuthorView>> AuthorsAsync(
        IReadOnlyList<TournamentPost> posts,
        CancellationToken cancellationToken)
    {
        var userIds = posts
            .SelectMany(post => post.Comments.Select(c => (Guid?)c.AuthorUserId).Append(post.AuthorUserId))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, FeedAuthorView>();
        }

        var accounts = await _directory.FindManyAsync(userIds, cancellationToken);
        var players = await _players.PlayerIdsOfAccountsAsync(userIds, cancellationToken);

        return accounts.ToDictionary(
            account => account.Id,
            account => new FeedAuthorView(
                account.Id,
                account.DisplayName ?? account.Email ?? "Unbekannt",
                players.GetValueOrDefault(account.Id)));
    }

    private FeedPostView ToView(
        TournamentPost post,
        IReadOnlyDictionary<Guid, FeedAuthorView> authors,
        Guid tournamentId) =>
        new(
            post.Id,
            post.Kind,
            post.AuthorUserId is { } author ? Author(author, authors) : null,
            post.Text,
            post.MatchId,
            post.CreatedAt,
            post.IsMessage && CanDelete(tournamentId, post.AuthorUserId),
            [.. post.Comments
                .OrderBy(comment => comment.CreatedAt)
                .Select(comment => ToComment(comment, authors, tournamentId))]);

    private FeedCommentView ToComment(
        PostComment comment,
        IReadOnlyDictionary<Guid, FeedAuthorView> authors,
        Guid tournamentId) =>
        new(
            comment.Id,
            Author(comment.AuthorUserId, authors),
            comment.Text,
            comment.CreatedAt,
            CanDelete(tournamentId, comment.AuthorUserId));

    /// <summary>
    /// Ein Verfasser, dessen Konto es nicht mehr gibt, hinterlässt seinen
    /// Beitrag — er ist Teil des Verlaufs. Nur sein Name fehlt dann.
    /// </summary>
    private static FeedAuthorView Author(Guid userId, IReadOnlyDictionary<Guid, FeedAuthorView> authors) =>
        authors.GetValueOrDefault(userId, new FeedAuthorView(userId, "Unbekannt", null));

    private bool CanDelete(Guid tournamentId, Guid? authorUserId)
    {
        var user = _userContext.Current;

        return (user.IsAuthenticated && authorUserId == user.UserId)
            || user.Can(Permission.ManageTournament, ResourceScope.Tournament(tournamentId));
    }

    /// <summary>
    /// Zeitgeordnete Kennungen (UUIDv7) und keine zufälligen.
    ///
    /// Der Feed wird nach <c>CreatedAt</c> sortiert, und zwei Einträge können
    /// denselben Zeitstempel tragen — ein Beitritt und die Meldung dazu
    /// entstehen im selben Aufruf, und eine gestellte Uhr macht es zum
    /// Normalfall. Als Stichentscheid taugt eine zufällige Guid nicht: sie
    /// dreht die Reihenfolge bei jedem Lauf anders herum. Eine UUIDv7 trägt
    /// ihre Entstehungszeit vorn und ordnet damit richtig — auch als Text, in
    /// dem SQLite sie ablegt.
    /// </summary>
    private static Guid NextId() => Guid.CreateVersion7();

    private void Announce(Guid tournamentId) =>
        _postCommit.Enqueue(ct => _notifier.FeedChangedAsync(tournamentId, ct));
}
