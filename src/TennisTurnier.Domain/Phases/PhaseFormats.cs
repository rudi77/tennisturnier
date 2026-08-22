using TennisTurnier.Domain.Common;
using TennisTurnier.Domain.Formats;

namespace TennisTurnier.Domain.Phases;

/// <summary>
/// Findet zu einer Formatart ihre Implementierung.
///
/// Die einzige Stelle im System, an der eine Fallunterscheidung über die
/// Formatart nötig ist. Jede weitere wäre ein Zeichen dafür, dass etwas an
/// <see cref="IPhaseFormat"/> vorbei entschieden wird.
/// </summary>
public static class PhaseFormats
{
    private static readonly IReadOnlyDictionary<PhaseFormatKind, IPhaseFormat> Implementations =
        new Dictionary<PhaseFormatKind, IPhaseFormat>
        {
            [PhaseFormatKind.Knockout] = new KnockoutFormat(),
            [PhaseFormatKind.RoundRobin] = new RoundRobinFormat(),
            [PhaseFormatKind.Swiss] = new SwissFormat(),
        };

    public static IPhaseFormat For(PhaseFormatKind kind)
    {
        if (Implementations.GetValueOrDefault(kind) is { } format)
        {
            return format;
        }

        throw new DomainException(
            $"Für das Format {kind} gibt es noch keine Implementierung. " +
            "Ein genuin neues Paarungsverfahren braucht eine eigene IPhaseFormat-Klasse (ADR-0001).");
    }

    public static bool IsSupported(PhaseFormatKind kind) => Implementations.ContainsKey(kind);
}
