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
        : this(settings, (client, string.Empty, settings.Model), loggers)
    {
    }

    private ModelAccess(AgentOptions settings, (IChatClient? Client, string Missing, string Model) connection, ILoggerFactory loggers)
    {
        _loggers = loggers;
        _settings = settings;
        Missing = connection.Missing;
        Model = connection.Model;

        // Einmal beim Start ins Protokoll, damit die Frage „ist die Variable
        // überhaupt angekommen?" nicht am Wert scheitert, den niemand zeigen
        // darf. Fehlt ein Name hier, erreicht die Variable den Prozess nicht —
        // dann liegt es an der Umgebung, nicht am Eingetragenen.
        // Das aufgelöste Modell gehört dazu: Ein Deployment, das es auf der
        // Ressource nicht gibt, scheitert sonst erst beim ersten Satz des
        // Benutzers — mit einem 404 tief im Stapel statt einer Zeile beim Start.
        loggers.CreateLogger<ModelAccess>().LogInformation(
            "Modellzugang {Zustand}, Modell „{Modell}\". Sichtbare Modellvariablen (nur Namen): {Variablen}",
            connection.Client is null ? "fehlt" : "steht",
            Model,
            SichtbareNamen());

        // Die Werkzeugschleife steckt in der Kette, nicht im Agenten: so bleibt
        // MaxToolRounds an einer Stelle, und der Agent zählt keine Runden selbst.
        _client = connection.Client?.AsBuilder()
            .UseFunctionInvocation(loggers, invoking => invoking.MaximumIterationsPerRequest = Math.Max(1, settings.MaxToolRounds))
            .Build();
    }

    /// <summary>Leer, wenn der Zugang steht; sonst der Satz für die Oberfläche.</summary>
    public string Missing { get; }

    /// <summary>Das aufgelöste Modell — bei Azure der Name des Deployments.</summary>
    public string Model { get; }

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

    private static (IChatClient? Client, string Missing, string Model) Connect(AgentOptions settings, IConfiguration configuration) =>
        settings.Provider == ModelProvider.OpenAI
            ? OpenAi(settings, configuration)
            : AzureOpenAi(settings, configuration);

    private static (IChatClient? Client, string Missing, string Model) OpenAi(AgentOptions settings, IConfiguration configuration)
    {
        var model = First(settings.Model, configuration["OpenAI:Model"], configuration["OPENAI_MODEL"]);
        var key = First(settings.ApiKey, configuration["OpenAI:ApiKey"], configuration["OPENAI_API_KEY"]);

        if (model is null)
        {
            return (null, $"Kein Modell konfiguriert (OPENAI_MODEL).{Anyway}", string.Empty);
        }

        return key is null
            ? (null, $"Kein Modellschlüssel konfiguriert (OPENAI_API_KEY).{Anyway}", string.Empty)
            : (new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(key)).AsIChatClient(), string.Empty, model);
    }

    private static (IChatClient? Client, string Missing, string Model) AzureOpenAi(AgentOptions settings, IConfiguration configuration)
    {
        var deployment = First(settings.Model, configuration["AzureOpenAI:Deployment"], configuration["AZURE_OPENAI_DEPLOYMENT"]);
        var endpoint = First(settings.Endpoint, configuration["AzureOpenAI:Endpoint"], configuration["AZURE_OPENAI_ENDPOINT"]);
        var key = First(settings.ApiKey, configuration["AzureOpenAI:ApiKey"], configuration["AZURE_OPENAI_API_KEY"]);

        if (deployment is null)
        {
            return (null, $"Kein Deployment konfiguriert (AZURE_OPENAI_DEPLOYMENT).{Anyway}", string.Empty);
        }

        if (endpoint is null)
        {
            return (null, $"Kein Azure-Endpunkt konfiguriert (AZURE_OPENAI_ENDPOINT).{Anyway}", string.Empty);
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return (null, $"„{endpoint}“ ist keine Adresse für AZURE_OPENAI_ENDPOINT.{Anyway}", string.Empty);
        }

        if (key is null && !settings.UseAzureCredential)
        {
            // „Ich habe den Schlüssel doch gesetzt" stimmt meistens — und trotzdem
            // steht er nicht zur Verfügung. Gesetzt und leer angekommen ist ein
            // anderer Fehler als nie angekommen: das eine ist ein Tippfehler im
            // Wert, das andere eine Variable, die den Prozess nicht erreicht.
            // Beides gleich zu benennen, schickt die Suche an die falsche Stelle.
            // Gefragt wird nach genau der Variablen, die der Satz auch nennt —
            // sonst spräche die Meldung von der einen und prüfte die andere.
            var vorhanden = configuration["AZURE_OPENAI_API_KEY"] is not null;

            return (null, vorhanden
                ? $"AZURE_OPENAI_API_KEY ist gesetzt, kommt hier aber leer an.{Anyway}"
                : $"Kein Zugang zu Azure OpenAI konfiguriert (AZURE_OPENAI_API_KEY, oder Agent__UseAzureCredential=true für die Anmeldung der Umgebung).{Anyway}", string.Empty);
        }

        // Ohne Schlüssel die Anmeldung der Umgebung: Managed Identity im
        // Betrieb, az login auf dem Rechner.
        var client = key is null
            ? new AzureOpenAIClient(uri, new DefaultAzureCredential())
            : new AzureOpenAIClient(uri, new ApiKeyCredential(key));

        return (client.GetChatClient(deployment).AsIChatClient(), string.Empty, deployment);
    }

    /// <summary>
    /// Wonach gesucht wird. Absichtlich grob: Ein Name, der nur beinahe stimmt,
    /// ist der häufigste Grund, warum eine gesetzte Variable nicht ankommt —
    /// und ein Filter, der exakt den richtigen Namen verlangt, versteckt
    /// ausgerechnet diesen Fall. <c>AZURE_OPEN_AI_API_KEY</c> soll hier
    /// auftauchen, nicht durchfallen.
    /// </summary>
    private static readonly string[] Verdaechtig = ["OPEN", "AZURE", "KEY"];

    /// <summary>
    /// Die Namen der Umgebungsvariablen, die das Modell betreffen könnten — nie
    /// ihre Werte. Steht ein erwarteter Name nicht dabei, erreicht er den
    /// Prozess nicht; steht er falsch geschrieben dabei, ist die Ursache
    /// gefunden.
    /// </summary>
    private static string SichtbareNamen() =>
        string.Join(
            ", ",
            Environment.GetEnvironmentVariables()
                .Keys
                .Cast<string>()
                .Where(name => Verdaechtig.Any(teil => name.Contains(teil, StringComparison.OrdinalIgnoreCase)))
                .Order(StringComparer.Ordinal));

    /// <summary>Der erste Wert, der wirklich einer ist.</summary>
    private static string? First(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
