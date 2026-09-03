using TennisTurnier.Application.Ports;

namespace TennisTurnier.Application.Tests.Fakes;

/// <summary>
/// Nimmt entgegen, was nach dem Commit schiefgegangen ist, und behält es.
///
/// Ein Nachlauf, der scheitert, darf den Schreibvorgang nicht kippen — er darf
/// aber auch nicht spurlos verschwinden. Beides ist nur zu prüfen, wenn ein
/// Test nachsehen kann, was gemeldet wurde.
/// </summary>
public sealed class SammelndeFehlermeldung : IPostCommitFailures
{
    private readonly List<Exception> _gemeldet = [];

    public IReadOnlyList<Exception> Gemeldet => _gemeldet;

    public void Report(Exception cause) => _gemeldet.Add(cause);
}
