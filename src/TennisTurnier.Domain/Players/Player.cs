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
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Player(Guid id) : base(id)
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        Contact = PlayerContact.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public PlayerContact Contact { get; private set; }

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
