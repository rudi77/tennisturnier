using System.Buffers.Text;
using System.Security.Cryptography;
using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Tournaments;

/// <summary>
/// Der Weg, auf dem sich jemand ohne Konto zu diesem Turnier meldet: ein Token,
/// dazu Kapazität und Meldeschluss.
///
/// Das Token entsteht mit dem Turnier und nicht erst beim Öffnen der Meldung.
/// So kann der Veranstalter den Link vorbereiten — auf den Aushang, in die
/// Vereinszeitung —, bevor die Meldung tatsächlich offen ist, und er überlebt
/// ein Zurücknehmen der Auslosung. Ob gemeldet werden kann, entscheidet der
/// Zustand des Turniers, nicht die Existenz des Links.
///
/// Es wird im Klartext geführt und nicht als Hash. Ein Hash machte es
/// unmöglich, den Link ein zweites Mal anzuzeigen — und genau das ist sein
/// Zweck. Das Token schützt auch keine Leserechte: was hinter ihm steht, sagt
/// nicht mehr als der Aushang. Es autorisiert allein das Schreiben einer
/// Meldung, und gegen ein Token in falschen Händen gibt es
/// <see cref="Rotated"/>.
/// </summary>
public sealed record RegistrationLink
{
    /// <summary>
    /// 128 Bit. Nicht zu erraten, und kurz genug, dass der Link auf einen
    /// Aushang passt und sich abtippen lässt.
    /// </summary>
    private const int TokenBytes = 16;

    private RegistrationLink(string token, int? capacity, DateTimeOffset? deadline)
    {
        Token = token;
        Capacity = Validate(capacity);
        Deadline = deadline;
    }

    public string Token { get; }

    /// <summary>
    /// Höchstzahl der Meldungen, die noch ins Feld rücken. Offen heißt: unbegrenzt,
    /// die Turnierleitung entscheidet von Hand.
    /// </summary>
    public int? Capacity { get; }

    /// <summary>Offen heißt: bis die Turnierleitung die Meldung schließt.</summary>
    public DateTimeOffset? Deadline { get; }

    public static RegistrationLink New(int? capacity = null, DateTimeOffset? deadline = null) =>
        new(NewToken(), capacity, deadline);

    /// <summary>Ändert die Bedingungen, lässt das Token stehen.</summary>
    public RegistrationLink With(int? capacity, DateTimeOffset? deadline) =>
        new(Token, capacity, deadline);

    /// <summary>
    /// Ein neues Token; das alte ist damit sofort wertlos. Der Notausgang, wenn
    /// der Link dort gelandet ist, wo er nicht hingehört.
    /// </summary>
    public RegistrationLink Rotated() => new(NewToken(), Capacity, Deadline);

    /// <summary>
    /// Ist das Feld voll? Eine Meldung darüber hinaus wird nicht abgewiesen,
    /// sondern landet auf der Warteliste — für den Melder ist das die bessere
    /// Antwort, und die Turnierleitung entscheidet ohnehin.
    /// </summary>
    public bool IsFull(int applied) => Capacity is { } capacity && applied >= capacity;

    public bool IsPastDeadline(DateTimeOffset now) => Deadline is { } deadline && now > deadline;

    private static string NewToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    private static int? Validate(int? capacity) =>
        capacity is < 1
            ? throw new DomainException($"Eine Kapazität beginnt bei 1, war {capacity}.")
            : capacity;
}
