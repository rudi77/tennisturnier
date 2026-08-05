using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>
/// Macht jeden angemeldeten Benutzer zum Veranstalter.
///
/// Damit ist die Frage beantwortet, an der eine frische Instanz bisher
/// stehenblieb: Rollen vergibt, wer eine Rolle hat, und nach einer Migration hat
/// niemand eine. Für den Systemadministrator löst das
/// <see cref="SystemAdminBootstrap"/> über die Konfiguration; für alle anderen
/// wäre dieselbe Antwort falsch — ein Veranstalter soll nicht in einer
/// Konfigurationsdatei stehen müssen, um ein Turnier auszuschreiben.
///
/// Die Rolle ist global und trotzdem harmlos: ihr einziges Recht ist
/// <see cref="Permission.CreateTournament"/>, und was daraus entsteht, gehört
/// ihrem Anleger allein — der Query-Filter zeigt ihm nur seine eigenen Turniere.
///
/// Sie wird ausdrücklich vergeben und nicht aus <c>IsAuthenticated</c>
/// abgeleitet: so steht sie in den Rollenzuweisungen, ist abfragbar, entziehbar
/// und über <c>Security:SelfServiceOrganizers</c> abschaltbar, wenn eine Instanz
/// geschlossen laufen soll. Eine unsichtbare Regel im Code wäre nichts davon.
/// </summary>
public sealed class OrganizerBootstrap
{
    private readonly BootstrapAdminOptions _options;
    private readonly IUserDirectory _directory;

    public OrganizerBootstrap(BootstrapAdminOptions options, IUserDirectory directory)
    {
        _options = options;
        _directory = directory;
    }

    /// <summary>
    /// Vergibt <see cref="Role.Organizer"/>, wenn der Selbstservice eingeschaltet
    /// ist und das Konto die Rolle noch nicht hat. Liefert <c>true</c>, wenn
    /// vergeben wurde — dann sind die übergebenen Zuweisungen veraltet.
    /// </summary>
    public async Task<bool> ApplyAsync(
        UserAccount account,
        IReadOnlyList<RoleAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assignments);

        if (!_options.SelfServiceOrganizers)
        {
            return false;
        }

        // Die bereits geladenen Zuweisungen entscheiden, nicht eine eigene
        // Abfrage — und der Systemadministrator braucht die Rolle nicht, er darf
        // ohnehin alles. Beides zusammen sorgt dafür, dass hier im Normalfall
        // kein einziger Zugriff auf die Datenbank entsteht: sonst läge bei jedem
        // Request ein zusätzlicher Schreibversuch an, und SQLite serialisiert
        // Schreiber datenbankweit.
        if (assignments.Any(a => a.Role is Role.Organizer or Role.SystemAdmin))
        {
            return false;
        }

        await _directory.AssignAsync(
            new RoleAssignment(Guid.NewGuid(), account.Id, Role.Organizer, ResourceScope.Global),
            cancellationToken);

        return true;
    }
}
