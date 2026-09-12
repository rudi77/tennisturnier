namespace Matchday.Server.Agent;

public sealed class AgentOptions
{
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>low, medium, high — ein Gespräch über ein Turnier braucht kein max.</summary>
    public string Effort { get; set; } = "medium";

    public int MaxTokens { get; set; } = 4096;

    /// <summary>Wie oft der Agent in einer Antwort Werkzeuge rufen darf, bevor er antworten muss.</summary>
    public int MaxToolRounds { get; set; } = 12;
}
