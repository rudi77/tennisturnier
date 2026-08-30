using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Tournaments;

namespace TennisTurnier.Domain.Social;

/// <summary>Wie jemand auf eine Einladung geantwortet hat.</summary>
public enum InvitationResponse
{
    /// <summary>Noch nicht geantwortet.</summary>
    Pending,

    Accepted,

    Declined,
}

/// <summary>
/// Eine Spielverabredung außerhalb jedes Turniers (ADR-0015).
///
/// Ein eigenes Wurzelaggregat neben <see cref="Tournament"/> und ausdrücklich
/// kein Turnier mit einem Match: sie kennt keine Phase, keinen Draw und kein
/// Ergebnis. ADR-0009 bleibt davon unberührt — dort steht, was die Wurzel
/// <em>eines Turniers</em> ist, nicht, dass es nur eine Sorte Wurzel gäbe.
///
/// Sie hat kein Ergebnis, und das ist die Entscheidung, die alles Weitere
/// einfach macht: es gibt niemanden, der ein Ergebnis bestätigt, und niemanden,
/// der eine Korrektur verantwortet. Ein Spielstand, den jeder über sich selbst
/// einträgt, wäre für eine Wertung wertlos — und eine Wertung wird hier nicht
/// gebaut.
/// </summary>
public sealed class PlayDate : Entity
{
    public const int MaxTitleLength = 120;

    public const int MaxNoteLength = 500;

    public const int MaxVenueLength = 120;

    /// <summary>
    /// So viele lassen sich einladen.
    ///
    /// Großzügig gegenüber dem, was gebraucht wird — für ein Doppel sucht man
    /// drei —, und trotzdem eine Grenze: eine Einladung an achtzig Leute ist
    /// keine Verabredung, sondern ein Aushang, und der hätte eigene Fragen zu
    /// beantworten (ADR-0015).
    /// </summary>
    public const int MaxInvitations = 20;

    private readonly List<PlayDateInvitation> _invitations = [];

    public PlayDate(
        Guid id,
        Guid hostUserId,
        string title,
        Discipline discipline,
        string venueName,
        DateTimeOffset startsAt,
        TimeSpan duration,
        string? note,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (hostUserId == Guid.Empty)
        {
            throw new DomainException("Eine Verabredung braucht einen Gastgeber.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("Eine Verabredung dauert länger als null Minuten.");
        }

        HostUserId = hostUserId;
        Title = Text(title, MaxTitleLength, "Der Titel", required: true)!;
        Discipline = discipline;
        VenueName = Text(venueName, MaxVenueLength, "Der Ort", required: true)!;
        StartsAt = startsAt;
        Duration = duration;
        Note = Text(note, MaxNoteLength, "Die Notiz", required: false);
        CreatedAt = createdAt;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private PlayDate(Guid id) : base(id)
    {
        Title = string.Empty;
        VenueName = string.Empty;
    }

    /// <summary>Wer eingeladen hat. Er zählt selbst mit — er spielt ja mit.</summary>
    public Guid HostUserId { get; private set; }

    public string Title { get; private set; }

    public Discipline Discipline { get; private set; }

    /// <summary>
    /// Wo gespielt wird — freier Text und kein Verweis auf einen Platz.
    ///
    /// Plätze gehören einem Turnier (ADR-0009), und eine Samstagsrunde hat
    /// keines. „TC Musterstadt, Platz 3" ist genau das, was man einander
    /// schreibt.
    /// </summary>
    public string VenueName { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public TimeSpan Duration { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Abgesagt. Das Einzige, was am Zustand gespeichert wird — alles andere
    /// ergibt sich aus den Antworten und der Uhr (ADR-0015).
    /// </summary>
    public bool IsCancelled { get; private set; }

    public IReadOnlyList<PlayDateInvitation> Invitations => _invitations;

    /// <summary>Einzel braucht zwei, Doppel und Mixed vier — der Gastgeber zählt mit.</summary>
    public int RequiredPlayers => Discipline == Discipline.Singles ? 2 : 4;

    /// <summary>Wie viele feststehen: der Gastgeber und alle, die zugesagt haben.</summary>
    public int Committed =>
        1 + _invitations.Count(invitation => invitation.Response == InvitationResponse.Accepted);

    /// <summary>Wie viele noch fehlen. Nie kleiner als null.</summary>
    public int Missing => Math.Max(0, RequiredPlayers - Committed);

    /// <summary>Steht die Runde?</summary>
    public bool IsConfirmed => !IsCancelled && Missing == 0;

    public DateTimeOffset EndsAt => StartsAt + Duration;

    /// <summary>
    /// Lädt jemanden ein.
    ///
    /// Dieselbe Person zweimal ist kein Fehler, sondern derselbe Klick — und
    /// die bestehende Einladung samt ihrer Antwort bleibt stehen. Sie zu
    /// ersetzen hieße, eine Absage stillschweigend zurückzunehmen.
    /// </summary>
    public PlayDateInvitation Invite(Guid id, Guid userId, Guid playerId)
    {
        RequireOpen();

        if (userId == Guid.Empty)
        {
            throw new DomainException("Eine Einladung braucht einen Empfänger.");
        }

        if (userId == HostUserId)
        {
            throw new DomainException("Der Gastgeber ist bereits dabei.");
        }

        if (_invitations.FirstOrDefault(invitation => invitation.UserId == userId) is { } existing)
        {
            return existing;
        }

        if (_invitations.Count >= MaxInvitations)
        {
            throw new DomainException(
                $"Eine Verabredung fasst höchstens {MaxInvitations} Einladungen.");
        }

        var invitation = new PlayDateInvitation(id, Id, userId, playerId);
        _invitations.Add(invitation);

        return invitation;
    }

    /// <summary>
    /// Zu- oder Absagen. Wer nicht eingeladen ist, kann nicht antworten — und
    /// erfährt das nicht hier, sondern schon daran, dass er die Verabredung
    /// nicht sieht.
    /// </summary>
    public void Respond(Guid userId, bool accepted)
    {
        RequireOpen();

        var invitation = _invitations.FirstOrDefault(i => i.UserId == userId)
            ?? throw new DomainException("Zu dieser Verabredung liegt keine Einladung vor.");

        // Absagen geht immer, zusagen nur solange Platz ist. Die eigene
        // bestehende Zusage zählt dabei nicht gegen einen — sonst ließe sich
        // eine Zusage nicht bestätigen, sobald die Runde voll ist.
        if (accepted
            && invitation.Response != InvitationResponse.Accepted
            && Missing == 0)
        {
            throw new DomainException("Die Runde ist bereits voll.");
        }

        invitation.Answer(accepted ? InvitationResponse.Accepted : InvitationResponse.Declined);
    }

    /// <summary>
    /// Sagt die Verabredung ab. Endgültig: eine wiederbelebte wäre eine, auf
    /// die jemand mit „hatte ich doch abgesagt" reagiert (ADR-0015).
    /// </summary>
    public void Cancel() => IsCancelled = true;

    private void RequireOpen()
    {
        if (IsCancelled)
        {
            throw new DomainException("Die Verabredung ist abgesagt.");
        }
    }

    private static string? Text(string? value, int max, string what, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? throw new DomainException($"{what} fehlt.") : null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= max
            ? trimmed
            : throw new DomainException($"{what} darf höchstens {max} Zeichen haben, war {trimmed.Length}.");
    }

    public override string ToString() => $"{Title} — {StartsAt:g} ({Committed}/{RequiredPlayers})";
}

/// <summary>
/// Eine Einladung zu einer Verabredung.
///
/// Sie trägt beides: das Konto, weil daran die Sichtbarkeit hängt und nur ein
/// Konto antworten kann, und den Spieler, weil aus ihm die Kontaktliste kommt
/// und der Weg ins Profil führt (ADR-0013).
/// </summary>
public sealed class PlayDateInvitation : Entity
{
    internal PlayDateInvitation(Guid id, Guid playDateId, Guid userId, Guid playerId)
        : base(id)
    {
        PlayDateId = playDateId;
        UserId = userId;
        PlayerId = playerId;
        Response = InvitationResponse.Pending;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private PlayDateInvitation(Guid id) : base(id)
    {
    }

    public Guid PlayDateId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PlayerId { get; private set; }

    public InvitationResponse Response { get; private set; }

    internal void Answer(InvitationResponse response) => Response = response;

    public override string ToString() => $"{UserId}: {Response}";
}
