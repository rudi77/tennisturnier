using TennisTurnier.Application.Ports;

namespace TennisTurnier.Api;

/// <summary>
/// Meldet einen gescheiterten Nachlauf ins Protokoll.
///
/// Als Warnung: der Schreibvorgang selbst war erfolgreich, es ist also kein
/// Fehler des Aufrufers. Aber der Push an die Zuschauer ist ausgefallen, und
/// die Anzeige im Vereinsheim hängt bis zum nächsten Abruf — das gehört
/// gesehen, nicht verschwiegen.
/// </summary>
internal sealed class PostCommitFailureLog : IPostCommitFailures
{
    private readonly ILogger<PostCommitFailureLog> _logger;

    public PostCommitFailureLog(ILogger<PostCommitFailureLog> logger) => _logger = logger;

    public void Report(Exception cause) =>
        _logger.LogWarning(
            cause,
            "Eine Handlung nach dem Speichern ist gescheitert. Der Schreibvorgang selbst steht — "
            + "betroffen ist, was danach hinausgehen sollte, etwa der Hinweis an die Zuschauer.");
}
