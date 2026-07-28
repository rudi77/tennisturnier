using TennisTurnier.Domain.Scheduling;

namespace TennisTurnier.Application.Ports;

/// <summary>
/// Erzeugt einen Spielplanvorschlag (ADR-0002).
///
/// Ein Port und keine Klasse, weil hier ausdrücklich mehr als ein Verfahren
/// denkbar ist: für Vereinsturniere genügt eine erklärbare Heuristik, für
/// größere Felder käme ein Constraint-Solver in Frage. Was sich dabei nicht
/// ändern darf, ist die Prüfung — sie liegt im
/// <see cref="ScheduleValidator"/> und nicht im Solver, damit ein Plan von Hand
/// nach denselben Regeln beurteilt wird wie ein gerechneter.
/// </summary>
public interface IScheduleSolver
{
    ScheduleProposal Solve(SchedulingProblem problem);
}
