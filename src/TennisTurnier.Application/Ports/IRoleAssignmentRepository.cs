using TennisTurnier.Domain.Security;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Rollenzuweisungen innerhalb einer laufenden Arbeitseinheit.
///
/// Es gibt dafür bereits <see cref="IUserDirectory"/> — der speichert aber
/// selbst. Das ist richtig für die Anmeldung, die außerhalb jeder
/// Arbeitseinheit läuft, und falsch für alles andere: wer ein Turnier anlegt,
/// muss im selben Zug sein Turnierleiter werden. Ginge die Zuweisung über einen
/// eigenen Speichervorgang, könnte das Turnier entstehen und die Rolle nicht —
/// und weil der Query-Filter nur noch die Turniere mit Rolle zeigt, wäre das
/// eben angelegte Turnier für seinen Anleger im nächsten Augenblick
/// unauffindbar.
/// </summary>
public interface IRoleAssignmentRepository
{
    void Add(RoleAssignment assignment);

    void Remove(RoleAssignment assignment);

    Task<IReadOnlyList<RoleAssignment>> ListByTournamentAsync(
        Guid tournamentId,
        CancellationToken cancellationToken = default);
}
