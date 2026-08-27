using TennisTurnier.Domain.Common;

namespace TennisTurnier.Domain.Security;

/// <summary>
/// Eine Einladung an eine Adresse, zu der es noch kein Konto gibt.
///
/// Der Punkt, an dem die Rollenvergabe bisher stehenblieb: berufen ließ sich
/// nur, wer sich schon einmal angemeldet hatte. Wer als Schiedsrichter
/// vorgesehen war, musste also erst kommen, bevor die Turnierleitung ihn
/// eintragen konnte — und wer eingeladen werden sollte, gar nicht erst.
/// ADR-0007 hat den Weg skizziert: eine Vorabzuweisung, die beim ersten Login
/// eingelöst wird. Das ist sie.
///
/// Sie verfällt nicht. Ein Verfall wäre ein eigener Zustand mit eigener Uhr und
/// eigener Erklärung dafür, warum eine Einladung plötzlich nichts mehr wert
/// ist; für einen Verein, der zwei Wochen vor dem Turnier einlädt, wäre das
/// Aufwand ohne Ertrag. Zurücknehmen lässt sie sich jederzeit — das ist der
/// Weg, der tatsächlich gebraucht wird.
///
/// Die Adresse wird kleingeschrieben gespeichert. Sie ist der Schlüssel zum
/// Einlösen, und „Anna@Verein.at" und „anna@verein.at" sind derselbe Mensch;
/// stünde beides nebeneinander, bekäme er seine Rolle je nach Schreibweise
/// seines Ausstellers oder eben nicht.
/// </summary>
public sealed class Invitation : Entity
{
    public Invitation(Guid id, Guid tournamentId, string email, Role role, DateTimeOffset createdAt)
        : base(id)
    {
        if (tournamentId == Guid.Empty)
        {
            throw new DomainException("Eine Einladung gehört zu einem Turnier.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Eine Einladung braucht eine E-Mail-Adresse.");
        }

        TournamentId = tournamentId;
        Email = email.Trim().ToLowerInvariant();
        Role = role;
        CreatedAt = createdAt;
    }

    /// <summary>Konstruktor für den Persistenzadapter.</summary>
    private Invitation(Guid id) : base(id) => Email = string.Empty;

    public Guid TournamentId { get; private set; }

    public string Email { get; private set; }

    public Role Role { get; private set; }

    /// <summary>
    /// Wann eingeladen wurde. Ohne Verfall ist das keine Frist, sondern eine
    /// Auskunft: „vor drei Monaten eingeladen, nie gekommen" ist der Grund,
    /// eine Einladung zurückzunehmen.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Die Rollenzuweisung, die aus ihr wird, sobald sich das Konto zum ersten
    /// Mal anmeldet.
    /// </summary>
    public RoleAssignment Redeem(Guid userId) =>
        new(Guid.NewGuid(), userId, Role, ResourceScope.Tournament(TournamentId));
}
