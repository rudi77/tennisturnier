using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Security;

/// <summary>
/// Löst offene Einladungen ein, sobald sich das Konto dazu zum ersten Mal
/// anmeldet.
///
/// Der Weg, den ADR-0007 skizziert hat und den es bis hierher nicht gab: eine
/// Vorabzuweisung per E-Mail-Adresse, eingelöst beim ersten Login. Sie steht
/// hier neben <see cref="SystemAdminBootstrap"/> und
/// <see cref="OrganizerBootstrap"/>, weil sie dieselbe Frage beantwortet — wer
/// bekommt beim Anmelden eine Rolle, die er vorher nicht hatte — und weil sie
/// an derselben Stelle in der Benutzerauflösung läuft.
///
/// Auf einen Schlag über alle Turniere: wer zu drei Turnieren eingeladen war,
/// bevor er ein Konto hatte, gehört nach seiner ersten Anmeldung zu allen drei.
/// Eine Einladung nach der anderen einzulösen hieße, ihm zwei davon so lange zu
/// verschweigen, bis er zufällig den richtigen Link öffnet.
///
/// Ohne E-Mail-Adresse am Konto passiert nichts. Nicht jeder Aussteller liefert
/// eine (Entra ID etwa oft nicht), und ohne sie fehlt der Schlüssel — die
/// Einladung bleibt dann stehen, statt an jemanden zu gehen, der sie nicht
/// bekommen sollte.
/// </summary>
public sealed class InvitationRedemption
{
    private readonly IInvitationRepository _invitations;
    private readonly IUserDirectory _directory;
    private readonly IUnitOfWork _unitOfWork;

    public InvitationRedemption(
        IInvitationRepository invitations,
        IUserDirectory directory,
        IUnitOfWork unitOfWork)
    {
        _invitations = invitations;
        _directory = directory;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Wandelt jede Einladung an die Adresse dieses Kontos in eine
    /// Rollenzuweisung. Liefert <c>true</c>, wenn etwas eingelöst wurde — dann
    /// sind die zuvor geladenen Zuweisungen veraltet.
    /// </summary>
    public async Task<bool> ApplyAsync(
        UserAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.Email))
        {
            return false;
        }

        var offen = await _invitations.ListByEmailAsync(account.Email, cancellationToken);

        if (offen.Count == 0)
        {
            return false;
        }

        foreach (var invitation in offen)
        {
            // Ohne Vorabprüfung, ob die Rolle schon da ist: das Verzeichnis
            // vergibt idempotent, und den Ausschlag gibt der eindeutige Index
            // über (Konto, Rolle, Scope). Eine zweite Prüfung hier wäre eine,
            // die nur bei einem Wettlauf falsch liegen könnte.
            await _directory.AssignAsync(invitation.Redeem(account.Id), cancellationToken);
        }

        // Dieselbe Überlegung für das Löschen, und aus demselben Anlass: die
        // Oberfläche stellt nach der Anmeldung mehrere Anfragen zugleich, jede
        // läuft hier durch, und zwei davon finden dieselbe offene Einladung.
        // Über die Änderungsverfolgung gelöscht, erwartete die zweite genau
        // eine getroffene Zeile, fände keine — und ein 409 landete auf einer
        // beliebigen anderen Anfrage, die mit Einladungen nichts zu tun hat.
        await _invitations.RemoveRedeemedAsync(
            [.. offen.Select(invitation => invitation.Id)], cancellationToken);

        // Die Zuweisung speichert das Verzeichnis selbst.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
