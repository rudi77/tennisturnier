using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

/// <summary>
/// Der lokale Abdruck eines Benutzers aus dem Identity Provider.
///
/// Er existiert, weil Rollenzuweisungen, Turniermeldungen und Ergebniseintragungen
/// auf eine stabile Id zeigen müssen. Der IdP liefert nur <see cref="SubjectId"/>;
/// alles Fachliche hängt an <see cref="Entity.Id"/>.
/// </summary>
public sealed class UserAccount : Entity
{
    public UserAccount(Guid id, string issuer, string subjectId, string? email, string? displayName)
        : base(id)
    {
        Issuer = Required(issuer, "Aussteller");
        SubjectId = Required(subjectId, "Subject");
        Email = Normalize(email);
        DisplayName = Normalize(displayName);
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private UserAccount(Guid id) : base(id)
    {
        Issuer = string.Empty;
        SubjectId = string.Empty;
    }

    /// <summary>
    /// Aussteller des Tokens. Zusammen mit <see cref="SubjectId"/> eindeutig —
    /// ein <c>sub</c> allein ist es nicht, sobald neben Keycloak auch Entra ID
    /// als Provider zugelassen ist.
    /// </summary>
    public string Issuer { get; private set; }

    public string SubjectId { get; private set; }

    public string? Email { get; private set; }

    public string? DisplayName { get; private set; }

    /// <summary>
    /// Übernimmt geänderte Angaben aus dem Token. Meldet, ob sich etwas geändert
    /// hat, damit der Aufrufer nicht bei jedem Request schreibt.
    /// </summary>
    public bool UpdateProfile(string? email, string? displayName)
    {
        var newEmail = Normalize(email);
        var newDisplayName = Normalize(displayName);

        if (newEmail == Email && newDisplayName == DisplayName)
        {
            return false;
        }

        Email = newEmail;
        DisplayName = newDisplayName;
        return true;
    }

    private static string Required(string value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"Ein Benutzerkonto braucht einen {what}-Wert.")
            : value.Trim();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
