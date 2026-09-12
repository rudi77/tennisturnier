using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Matchday.Server.Tests;

/// <summary>
/// Ein Modell, das nicht denkt, sondern vorliest: je Runde eine Liste von
/// Inhalten. Damit lässt sich die Werkzeugschleife ohne Netz prüfen — und
/// nachsehen, was das Modell in jeder Runde zu lesen bekam.
/// </summary>
internal sealed class Modell(params IReadOnlyList<AIContent>[] runden) : IChatClient
{
    /// <summary>Die Nachrichten jeder Runde, in der Reihenfolge der Aufrufe.</summary>
    public List<List<ChatMessage>> Anfragen { get; } = [];

    /// <summary>Was mitgeschickt wurde: Anweisungen, Grenzen, Werkzeuge.</summary>
    public ChatOptions? Optionen { get; private set; }

    /// <summary>Ist sie gesetzt, fliegt sie in <see cref="FehlerInRunde"/>.</summary>
    public Exception? Fehler { get; init; }

    public int FehlerInRunde { get; init; } = 1;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Der Agent streamt.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Anfragen.Add([.. messages]);
        Optionen = options;
        var runde = Anfragen.Count;

        if (Fehler is not null && runde == FehlerInRunde)
        {
            throw Fehler;
        }

        await Task.Yield();

        // Nach dem letzten Drehbuch antwortet das Modell und hört auf: sonst
        // liefe die Schleife bis an ihre Grenze.
        var inhalte = runde <= runden.Length ? runden[runde - 1] : [new TextContent("Fertig.")];

        for (var i = 0; i < inhalte.Count; i++)
        {
            var inhalt = inhalte[i];
            yield return new ChatResponseUpdate(ChatRole.Assistant, [inhalt])
            {
                FinishReason = inhalt is FunctionCallContent ? ChatFinishReason.ToolCalls
                    : i == inhalte.Count - 1 ? ChatFinishReason.Stop
                    : null,
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
