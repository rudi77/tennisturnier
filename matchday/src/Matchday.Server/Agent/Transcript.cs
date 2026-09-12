using System.Text.Json;
using System.Text.Json.Serialization;

namespace Matchday.Server.Agent;

/// <summary>
/// Der gespeicherte Gesprächsverlauf einer Sitzung — in einer eigenen Form,
/// damit der Speicher nicht an den SDK-Typen hängt. Thinking-Blöcke bleiben
/// samt Signatur erhalten und werden unverändert zurückgegeben.
/// </summary>
public sealed class ChatSession
{
    public required string Id { get; init; }

    public required string ClientId { get; init; }

    public Guid? TournamentId { get; set; }

    public List<StoredMessage> Messages { get; init; } = [];
}

public sealed class StoredMessage
{
    public required string Role { get; init; }

    public List<StoredBlock> Blocks { get; init; } = [];
}

public sealed class StoredBlock
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required BlockKind Kind { get; init; }

    public string? Text { get; init; }

    public string? Signature { get; init; }

    public string? Id { get; init; }

    public string? Name { get; init; }

    public JsonElement? Input { get; init; }

    public string? ToolUseId { get; init; }

    public bool IsError { get; init; }

    /// <summary>Nur für die Oberfläche beim Nachladen: welches Widget zu diesem Werkzeugergebnis gehört.</summary>
    public string? Widget { get; init; }
}

public enum BlockKind
{
    Text,
    Thinking,
    ToolUse,
    ToolResult,
}
