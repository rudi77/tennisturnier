using TennisTurnier.Application.Ports;

namespace TennisTurnier.Api;

/// <summary>
/// Die Systemuhr — der einzige Ort im Programm, der sie liest.
///
/// Sie steht in der Composition Root und nicht in der Anwendungsschicht: eine
/// Uhr ist Infrastruktur, und dass die Anwendung sie nur als Port kennt, ist der
/// Grund, warum sich ein Turniertag in Tests durchspielen lässt, ohne zu warten.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
