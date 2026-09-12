using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Live;
using Matchday.Server.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Matchday.Server.Tests;

/// <summary>Ein frischer Speicher in einer Temporärdatei je Test, mit allem, was darauf sitzt.</summary>
public sealed class Aufbau : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-test-{Guid.NewGuid():N}.db");

    public Aufbau()
    {
        // Pooling=False: Ohne Pool gibt es nichts zu leeren, und die Datei ist
        // sofort nach dem letzten Befehl frei. Der frühere Weg — beim Aufräumen
        // SqliteConnection.ClearAllPools() — wirkt auf den ganzen Prozess und
        // damit auch auf die Testklassen, die xUnit gerade parallel laufen
        // lässt: Eine Klasse räumte ab und zog den anderen die Verbindung weg.
        Store = new TournamentStore($"Data Source={_path};Pooling=False");
        Live = new LiveHub();
        Actions = new TournamentActions(Store, Live, TimeProvider.System);
        Tools = new AgentTools(Actions);
    }

    public TournamentStore Store { get; }

    public LiveHub Live { get; }

    public TournamentActions Actions { get; }

    public AgentTools Tools { get; }

    public Actor Rudi { get; } = new("browser-rudi", null);

    public Actor Fremder { get; } = new("browser-fremd", null);

    /// <summary>Der Agent mit einem vorlesenden Modell statt einem echten.</summary>
    public TournamentAgent Agent(IChatClient modell, AgentOptions? einstellungen = null) =>
        new(Tools,
            Actions,
            Store,
            new ModelAccess(einstellungen ?? new AgentOptions(), modell, NullLoggerFactory.Instance),
            TimeProvider.System,
            NullLogger<TournamentAgent>.Instance);

    public void Dispose() => Wegräumen(_path);

    /// <summary>
    /// Eine Datei im Temp-Ordner, die einmal liegen bleibt, ist kein Grund,
    /// einen grünen Lauf rot zu machen.
    /// </summary>
    internal static void Wegräumen(string pfad)
    {
        try
        {
            File.Delete(pfad);
        }
        catch (IOException)
        {
        }
    }
}
