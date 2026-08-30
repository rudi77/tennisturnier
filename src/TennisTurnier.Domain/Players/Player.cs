using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Players;

/// <summary>
/// Ein Spieler. Existiert vereinsübergreifend (ADR-0008) und trägt deshalb
/// bewusst keine <c>ClubId</c> — ein Gastspieler ist damit kein Sonderfall,
/// sondern schlicht jemand ohne Mitgliedschaft im ausrichtenden Verein.
///
/// Der Preis: der Query-Filter aus ADR-0004 greift hier nicht. Kontaktdaten
/// stehen deshalb in <see cref="Contact"/> gebündelt, damit die eine Stelle
/// sichtbar bleibt, die nicht in eine öffentliche Ausgabe gehört.
///
/// Seit dem Beitritt über ein Konto kann ein Spieler zu einem
/// <see cref="UserAccountId"/> gehören — er muss es aber nicht. Wen die
/// Turnierleitung aus einer Liste einliest, hat kein Konto und soll auch
/// keines brauchen; er spielt trotzdem mit.
/// </summary>
public sealed class Player : Entity
{
    public Player(Guid id, string firstName, string lastName, PlayerContact? contact = null)
        : base(id)
    {
        FirstName = Required(firstName, "Vorname");
        LastName = Required(lastName, "Nachname");
        Contact = contact ?? PlayerContact.Empty;
        Profile = PlayerProfile.Empty;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Player(Guid id) : base(id)
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        Contact = PlayerContact.Empty;
        Profile = PlayerProfile.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public PlayerContact Contact { get; private set; }

    /// <summary>
    /// Was der Spieler über sich selbst geschrieben hat (ADR-0013).
    ///
    /// Der einzige Teil eines Spielers, den niemand berechnen kann — alles
    /// andere an einem Profil kommt aus gespielten Matches. Er gehört dem
    /// Konto hinter dem Spieler und sonst niemandem.
    /// </summary>
    public PlayerProfile Profile { get; private set; }

    /// <summary>
    /// Das Konto, dem dieser Spieler gehört — oder <c>null</c>.
    ///
    /// Die Brücke zwischen den beiden Welten, die es bis hierher nur getrennt
    /// gab: Konten wussten nichts von Spielern, Spieler nichts von Konten.
    /// Wer über einen Beitrittslink mitspielt, verbindet beide; wer aus einer
    /// hochgeladenen Liste kommt, bleibt vorerst ohne.
    ///
    /// Nicht am Teilnehmer und nicht an der Meldung: das Konto gehört zur
    /// Person und nicht zu ihrem Auftritt in einem bestimmten Turnier.
    /// </summary>
    public Guid? UserAccountId { get; private set; }

    /// <summary>Die einzige Form des Namens, die in eine öffentliche Ausgabe gehört.</summary>
    public string DisplayName => $"{LastName}, {FirstName}";

    /// <summary>
    /// Verbindet diesen Spieler mit einem Konto.
    ///
    /// Dieselbe Verbindung zweimal ist kein Fehler, sondern derselbe Mensch,
    /// der ein zweites Mal beitritt. Eine <em>andere</em> schon: dann steht
    /// entweder ein Namensvetter unter fremder Adresse oder zwei Menschen
    /// teilen sich einen Spieler — beides will bemerkt und nicht stillschweigend
    /// überschrieben werden.
    /// </summary>
    public void LinkAccount(Guid userAccountId)
    {
        if (userAccountId == Guid.Empty)
        {
            throw new DomainException("Ein Spieler lässt sich nicht mit einem leeren Konto verbinden.");
        }

        if (UserAccountId is { } bestehend && bestehend != userAccountId)
        {
            throw new DomainException(
                $"Der Spieler {DisplayName} gehört bereits einem anderen Konto.");
        }

        UserAccountId = userAccountId;
    }

    /// <summary>
    /// Berichtigt den Namen.
    ///
    /// Ohne Wirkung auf die Vergangenheit: der Anzeigename eines Teilnehmers
    /// wird beim Melden festgeschrieben, damit eine spätere Umbenennung die
    /// Ergebnisliste eines abgeschlossenen Turniers nicht rückwirkend ändert.
    /// Wer heiratet, heißt in der Tabelle vom Frühjahr weiterhin, wie er dort
    /// angetreten ist.
    /// </summary>
    public void Rename(string firstName, string lastName)
    {
        FirstName = Required(firstName, "Vorname");
        LastName = Required(lastName, "Nachname");
    }

    /// <summary>
    /// Übernimmt, was der Spieler über sich geschrieben hat.
    ///
    /// Wer das darf, entscheidet der Anwendungsfall — hier steht nur, dass ein
    /// Spieler ohne Konto niemandem gehört und deshalb auch von niemandem
    /// beschrieben wird. Ohne diese Prüfung könnte die Turnierleitung, die eine
    /// Liste eingelesen hat, den Eingelesenen Sätze in den Mund legen.
    /// </summary>
    public void Describe(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (UserAccountId is null)
        {
            throw new DomainException(
                $"Der Spieler {DisplayName} gehört keinem Konto und hat deshalb kein Profil zu pflegen.");
        }

        Profile = profile;
    }

    private static string Required(string value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"Ein Spieler braucht einen {what}.")
            : value.Trim();
}

/// <summary>
/// Kontaktdaten, absichtlich als eigener Typ.
///
/// Sie sind der Teil eines Spielers, der niemals in der öffentlichen Projektion
/// landen darf (ADR-0003). Als Bündel sind sie beim Abbilden auf ein DTO schwer
/// zu übersehen — als drei lose Felder am Spieler wären sie es nicht.
/// </summary>
public sealed record PlayerContact(string? Email, string? Phone, DateOnly? DateOfBirth)
{
    public static PlayerContact Empty { get; } = new(null, null, null);
}

/// <summary>
/// Was ein Spieler über sich selbst sagt — und ausdrücklich das Gegenteil von
/// <see cref="PlayerContact"/>.
///
/// Kontaktdaten sind das, was nie in eine Ausgabe für andere gehört; das hier
/// ist eigens dafür geschrieben. Beides in einem Typ, und die eine Regel wäre
/// beim Abbilden auf ein DTO nicht mehr von der anderen zu unterscheiden
/// (ADR-0013).
/// </summary>
/// <param name="Bio">Ein paar Sätze über sich. Leer ist der Normalfall.</param>
/// <param name="HomeClub">
/// Der Heimatverein als freier Text und nicht als Verweis: den Verein als
/// Aggregat gibt es seit ADR-0009 nicht mehr, und ihn für diese eine Zeile
/// wiederzubeleben hieße, die Entscheidung zurückzunehmen.
/// </param>
public sealed record PlayerProfile(string? Bio, string? HomeClub)
{
    /// <summary>So lang, dass zwei, drei Sätze hineinpassen — und nicht länger.</summary>
    public const int MaxBioLength = 500;

    public const int MaxHomeClubLength = 120;

    public static PlayerProfile Empty { get; } = new(null, null);

    /// <summary>
    /// Prüft und beschneidet, was von außen hereinkommt. Der Konstruktor bleibt
    /// unangetastet, damit der Persistenzadapter Altdaten laden kann, die
    /// länger sind als eine später verschärfte Grenze.
    /// </summary>
    public static PlayerProfile From(string? bio, string? homeClub) =>
        new(Limit(bio, MaxBioLength, "Der Text über sich"),
            Limit(homeClub, MaxHomeClubLength, "Der Heimatverein"));

    private static string? Limit(string? value, int max, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= max
            ? trimmed
            : throw new DomainException($"{what} darf höchstens {max} Zeichen haben, war {trimmed.Length}.");
    }
}
