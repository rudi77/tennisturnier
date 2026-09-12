using Matchday.Server.Agent;
using Matchday.Server.Api;
using Matchday.Server.Live;
using Matchday.Server.Storage;

namespace Matchday.Server.Tests;

/// <summary>Ein frischer Speicher in einer Temporärdatei je Test, mit allem, was darauf sitzt.</summary>
public sealed class Aufbau : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-test-{Guid.NewGuid():N}.db");

    public Aufbau()
    {
        Store = new TournamentStore($"Data Source={_path}");
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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
