namespace Matchday.Server.Agent;

/// <summary>Wer das Modell stellt. Azure ist die Vorgabe (ADR-0017).</summary>
public enum ModelProvider
{
    AzureOpenAI,
    OpenAI,
}

public sealed class AgentOptions
{
    public ModelProvider Provider { get; set; } = ModelProvider.AzureOpenAI;

    /// <summary>Bei Azure der Name des Deployments, bei OpenAI der Modellname.</summary>
    public string Model { get; set; } = "gpt-5";

    /// <summary>Nur bei Azure: die Adresse der Ressource. Sonst ohne Bedeutung.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Der Schlüssel. Fehlt er, greifen die Umgebungsvariablen.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Statt eines Schlüssels die Anmeldung der Umgebung benutzen — Managed
    /// Identity im Betrieb, <c>az login</c> auf dem Rechner.
    /// </summary>
    public bool UseAzureCredential { get; set; }

    /// <summary>
    /// low, medium, high, max — ein Gespräch über ein Turnier braucht kein max.
    /// Leer heißt: gar nichts mitschicken, für Modelle ohne Reasoning.
    /// </summary>
    public string Effort { get; set; } = "medium";

    public int MaxTokens { get; set; } = 4096;

    /// <summary>Wie oft der Agent in einer Antwort Werkzeuge rufen darf, bevor er antworten muss.</summary>
    public int MaxToolRounds { get; set; } = 12;
}
