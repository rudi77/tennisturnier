using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Agent;

/// <summary>
/// Der Zugang zum Modell. Er entsteht einmal beim Start: entweder steht er,
/// oder <see cref="Missing"/> sagt in einem Satz, was fehlt — dann läuft alles
/// außer dem Eingabefeld weiter (ADR-0017).
/// </summary>
public sealed class ModelAccess
{
    private const string Anyway = " Die Widgets funktionieren trotzdem.";

    private readonly IChatClient? _client;
    private readonly ILoggerFactory _loggers;
    private readonly AgentOptions _settings;

    public ModelAccess(IOptions<AgentOptions> options, IConfiguration configuration, ILoggerFactory loggers)
        : this(options.Value, Connect(options.Value, configuration), loggers)
    {
    }

    /// <summary>Für die Tests: ein Modell von Hand statt eines aus der Konfiguration.</summary>
    internal ModelAccess(AgentOptions settings, IChatClient client, ILoggerFactory loggers)
        : this(settings, (client, string.Empty), loggers)
    {
    }

    private ModelAccess(AgentOptions settings, (IChatClient? Client, string Missing) connection, ILoggerFactory loggers)
    {
        _loggers = loggers;
        _settings = settings;
        Missing = connection.Missing;

        // Die Werkzeugschleife steckt in der Kette, nicht im Agenten: so bleibt
        // MaxToolRounds an einer Stelle, und der Agent zählt keine Runden selbst.
        _client = connection.Client?.AsBuilder()
            .UseFunctionInvocation(loggers, invoking => invoking.MaximumIterationsPerRequest = Math.Max(1, settings.MaxToolRounds))
            .Build();
    }

    /// <summary>Leer, wenn der Zugang steht; sonst der Satz für die Oberfläche.</summary>
    public string Missing { get; }

    public bool IsConfigured => _client is not null;

    /// <summary>
    /// Ein Agent für eine Anfrage. Die Werkzeuge wechseln von Anfrage zu
    /// Anfrage — sie tragen den Aufrufer und das aktuelle Turnier mit sich.
    /// </summary>
    public AIAgent CreateAgent(string instructions, IList<AITool> tools)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Ohne Modellzugang gibt es keinen Agenten.");
        }

        return new ChatClientAgent(
            _client,
            new ChatClientAgentOptions
            {
                Name = "MATCHDAY",
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    MaxOutputTokens = _settings.MaxTokens,
                    Reasoning = ReasoningFor(_settings.Effort),
                    Tools = tools,
                },

                // Die Kette ist oben schon gebaut; der Agent soll keine zweite
                // Werkzeugschleife darüberlegen.
                UseProvidedChatClientAsIs = true,
            },
            _loggers);
    }

    internal static ReasoningOptions? ReasoningFor(string effort) => effort.Trim().ToLowerInvariant() switch
    {
        "" or "none" => null,
        "low" => new ReasoningOptions { Effort = ReasoningEffort.Low },
        "high" => new ReasoningOptions { Effort = ReasoningEffort.High },
        "max" => new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh },
        _ => new ReasoningOptions { Effort = ReasoningEffort.Medium },
    };

    private static (IChatClient? Client, string Missing) Connect(AgentOptions settings, IConfiguration configuration) =>
        settings.Provider == ModelProvider.OpenAI
            ? OpenAi(settings, configuration)
            : AzureOpenAi(settings, configuration);

    private static (IChatClient? Client, string Missing) OpenAi(AgentOptions settings, IConfiguration configuration)
    {
        var model = First(settings.Model, configuration["OpenAI:Model"], configuration["OPENAI_MODEL"]);
        var key = First(settings.ApiKey, configuration["OpenAI:ApiKey"], configuration["OPENAI_API_KEY"]);

        if (model is null)
        {
            return (null, $"Kein Modell konfiguriert (OPENAI_MODEL).{Anyway}");
        }

        return key is null
            ? (null, $"Kein Modellschlüssel konfiguriert (OPENAI_API_KEY).{Anyway}")
            : (new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(key)).AsIChatClient(), string.Empty);
    }

    private static (IChatClient? Client, string Missing) AzureOpenAi(AgentOptions settings, IConfiguration configuration)
    {
        var deployment = First(settings.Model, configuration["AzureOpenAI:Deployment"], configuration["AZURE_OPENAI_DEPLOYMENT"]);
        var endpoint = First(settings.Endpoint, configuration["AzureOpenAI:Endpoint"], configuration["AZURE_OPENAI_ENDPOINT"]);
        var key = First(settings.ApiKey, configuration["AzureOpenAI:ApiKey"], configuration["AZURE_OPENAI_API_KEY"]);

        if (deployment is null)
        {
            return (null, $"Kein Deployment konfiguriert (AZURE_OPENAI_DEPLOYMENT).{Anyway}");
        }

        if (endpoint is null)
        {
            return (null, $"Kein Azure-Endpunkt konfiguriert (AZURE_OPENAI_ENDPOINT).{Anyway}");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return (null, $"„{endpoint}“ ist keine Adresse für AZURE_OPENAI_ENDPOINT.{Anyway}");
        }

        if (key is null && !settings.UseAzureCredential)
        {
            return (null, $"Kein Zugang zu Azure OpenAI konfiguriert (AZURE_OPENAI_API_KEY, oder Agent__UseAzureCredential=true für die Anmeldung der Umgebung).{Anyway}");
        }

        // Ohne Schlüssel die Anmeldung der Umgebung: Managed Identity im
        // Betrieb, az login auf dem Rechner.
        var client = key is null
            ? new AzureOpenAIClient(uri, new DefaultAzureCredential())
            : new AzureOpenAIClient(uri, new ApiKeyCredential(key));

        return (client.GetChatClient(deployment).AsIChatClient(), string.Empty);
    }

    /// <summary>Der erste Wert, der wirklich einer ist.</summary>
    private static string? First(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
