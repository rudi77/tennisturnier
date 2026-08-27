using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Einladungen, die auf ihr Konto warten.
///
/// Wie <see cref="IRoleAssignmentRepository"/> ohne eigenen Speichervorgang:
/// eine Einladung entsteht und vergeht in derselben Arbeitseinheit wie die
/// Rollenzuweisung, aus der sie wird — sonst gäbe es einen Augenblick, in dem
/// jemand beides hat oder keines von beidem.
///
/// Auf der Tabelle liegt kein Query-Filter. Sie ist wie die Rollenzuweisungen
/// die Grundlage der Sichtbarkeit und könnte nicht von ihr abhängen, ohne sich
/// im Kreis zu drehen; die Berechtigung prüft der Anwendungsfall, bevor er
/// hier hereinkommt.
/// </summary>
public interface IInvitationRepository
{
    void Add(Invitation invitation);

    void Remove(Invitation invitation);

    Task<IReadOnlyList<Invitation>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Alle Einladungen an diese Adresse — über Turniere hinweg.
    ///
    /// Der Weg beim ersten Login: wer eingeladen wurde, bevor er ein Konto
    /// hatte, wird auf einen Schlag Mitglied überall dort, wo man auf ihn
    /// gewartet hat.
    /// </summary>
    Task<IReadOnlyList<Invitation>> ListByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
}
