using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>Was die Einlösung ergeben hat.</summary>
public enum BootstrapOutcome
{
    /// <summary>Niemand konfiguriert — der Normalfall in einem laufenden System.</summary>
    NotConfigured,

    /// <summary>Der Benutzer ist bereits Systemadministrator.</summary>
    AlreadyAdmin,

    /// <summary>
    /// Es ist jemand konfiguriert, aber nicht dieser Benutzer.
    ///
    /// Eigener Fall und nicht mit <see cref="NotConfigured"/> zusammengefasst,
    /// weil genau hier die Einrichtung schiefgeht: Wer eine E-Mail einträgt, die
    /// der Aussteller nicht ins Token legt, bekommt sonst keinerlei Hinweis
    /// darauf, dass er auf etwas wartet, das nie eintritt.
    /// </summary>
    NotListed,

    /// <summary>Die Rolle wurde soeben vergeben.</summary>
    Granted,
}

/// <summary>
/// Löst die vorab konfigurierten Systemadministratoren ein.
///
/// ADR-0007 hält die Rollen bewusst in der Anwendung und nicht im IdP. Damit
/// entsteht ein Henne-Ei-Problem: Rollen vergibt, wer eine Rolle hat, und nach
/// einer frischen Migration hat niemand eine. Die Konfiguration ist der einzige
/// Kanal, der außerhalb der Datenbank liegt und deshalb gefüllt sein kann,
/// bevor sich zum ersten Mal jemand anmeldet.
///
/// Eingelöst wird bei der Anmeldung und nicht beim Start des Prozesses: das
/// lokale Konto entsteht erst mit dem ersten Request (ADR-0007, Konsequenzen).
/// Vorher gibt es niemanden, dem die Rolle gehören könnte.
/// </summary>
public sealed class SystemAdminBootstrap
{
    private readonly BootstrapAdminOptions _options;
    private readonly IUserDirectory _directory;

    public SystemAdminBootstrap(BootstrapAdminOptions options, IUserDirectory directory)
    {
        _options = options;
        _directory = directory;
    }

    /// <summary>
    /// Vergibt dem Konto die globale Rolle <see cref="Role.SystemAdmin"/>, wenn es
    /// in der Konfiguration steht und sie noch nicht hat.
    ///
    /// Bei <see cref="BootstrapOutcome.Granted"/> sind die übergebenen Zuweisungen
    /// veraltet und der Aufrufer muss sie neu laden.
    /// </summary>
    public async Task<BootstrapOutcome> ApplyAsync(
        UserAccount account,
        IReadOnlyList<RoleAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assignments);

        // Der Normalfall in jedem laufenden System: nichts konfiguriert. Die
        // Prüfung steht vor allen anderen, damit sie nichts kostet — sie läuft
        // bei jedem Request, für jeden Benutzer.
        if (_options.BootstrapSystemAdmins.Count == 0)
        {
            return BootstrapOutcome.NotConfigured;
        }

        // Die bereits geladenen Zuweisungen entscheiden, nicht eine eigene
        // Abfrage: sonst läge für den Administrator bei jedem einzelnen Request
        // ein zusätzlicher Lesezugriff auf der Datenbank.
        if (assignments.Any(a => a.Role == Role.SystemAdmin))
        {
            return BootstrapOutcome.AlreadyAdmin;
        }

        if (!IsListed(account))
        {
            return BootstrapOutcome.NotListed;
        }

        await _directory.AssignAsync(
            new RoleAssignment(Guid.NewGuid(), account.Id, Role.SystemAdmin, ResourceScope.Global),
            cancellationToken);

        return BootstrapOutcome.Granted;
    }

    /// <summary>
    /// Erkannt wird die E-Mail-Adresse oder die Subject-ID.
    ///
    /// Die E-Mail, weil ADR-0007 sie als Weg für Vorab-Zuweisungen benennt und
    /// weil eine Keycloak-GUID niemand von Hand in eine Konfiguration schreibt.
    /// Die Subject-ID, weil nicht jeder Aussteller eine E-Mail in das Token legt
    /// — ohne sie bliebe ein solcher Verbund ohne ersten Administrator.
    /// </summary>
    private bool IsListed(UserAccount account) =>
        _options.BootstrapSystemAdmins.Any(entry =>
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            var trimmed = entry.Trim();

            // E-Mail-Adressen ohne Rücksicht auf Groß- und Kleinschreibung, die
            // Subject-ID genau: sie ist eine undurchsichtige Kennung, und zwei,
            // die sich nur in der Schreibweise unterscheiden, sind zwei.
            return string.Equals(trimmed, account.Email, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(trimmed, account.SubjectId, StringComparison.Ordinal);
        });
}
