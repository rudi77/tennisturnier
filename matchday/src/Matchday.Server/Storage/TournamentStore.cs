using System.Text.Json;
using System.Text.Json.Serialization;
using Matchday.Domain;
using Microsoft.Data.Sqlite;

namespace Matchday.Server.Storage;

/// <summary>
/// SQLite als Dokumentspeicher: ein Turnier ist eine JSON-Zeile (ADR-0016).
/// Lesen, Ändern und Schreiben laufen unter einer Sperre — ein Prozess, eine
/// Datei, keine verlorenen Ergebnisse, wenn zwei Handys gleichzeitig eintragen.
/// </summary>
public sealed class TournamentStore
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TournamentStore(string connectionString)
    {
        _connectionString = connectionString;
        Initialize();
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tournaments (
                id TEXT PRIMARY KEY,
                owner_id TEXT NOT NULL,
                admin_token TEXT NOT NULL UNIQUE,
                json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_tournaments_owner ON tournaments(owner_id);
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                client_id TEXT NOT NULL,
                json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // Spalten, die später dazukamen. Eine bestehende Datei bekommt sie
        // nachgetragen, und was sie tragen sollen, steht schon in der Zeile.
        if (AddColumn(connection, "tournaments", "scorer_token"))
        {
            Backfill(connection, "scorer_token", t => t.ScorerToken);
        }

        if (AddColumn(connection, "tournaments", "viewer_token"))
        {
            Backfill(connection, "viewer_token", t => t.ViewerToken);
        }

        if (AddColumn(connection, "sessions", "tournament_id"))
        {
            using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE sessions SET tournament_id = json_extract(json, '$.tournamentId')";
            backfill.ExecuteNonQuery();
        }

        using var indexes = connection.CreateCommand();
        indexes.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_tournaments_scorer ON tournaments(scorer_token);
            CREATE INDEX IF NOT EXISTS ix_tournaments_viewer ON tournaments(viewer_token);
            CREATE INDEX IF NOT EXISTS ix_sessions_tournament ON sessions(client_id, tournament_id);
            """;
        indexes.ExecuteNonQuery();
    }

    /// <summary>Legt eine Spalte an, wenn es sie noch nicht gibt — und sagt, ob sie neu ist.</summary>
    private static bool AddColumn(SqliteConnection connection, string table, string column)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";

        if ((long)check.ExecuteScalar()! > 0)
        {
            return false;
        }

        using var add = connection.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT";
        add.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// Eine Spalte, die nur zum Finden da ist, aus der Zeile selbst füllen: Was
    /// sie tragen soll, weiß das Turnier schon.
    /// </summary>
    private static void Backfill(SqliteConnection connection, string column, Func<Tournament, string> value)
    {
        var tokens = new List<(string Id, string Token)>();

        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT id, json FROM tournaments";
            using var reader = read.ExecuteReader();

            while (reader.Read())
            {
                tokens.Add((reader.GetString(0), value(Deserialize(reader.GetString(1))!)));
            }
        }

        foreach (var (id, token) in tokens)
        {
            using var write = connection.CreateCommand();
            write.CommandText = $"UPDATE tournaments SET {column} = $token WHERE id = $id";
            write.Parameters.AddWithValue("$token", token);
            write.Parameters.AddWithValue("$id", id);
            write.ExecuteNonQuery();
        }
    }

    public async Task<Tournament?> FindAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM tournaments WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return Deserialize(await command.ExecuteScalarAsync(ct) as string);
    }

    public async Task<Tournament?> FindByAdminTokenAsync(string adminToken, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM tournaments WHERE admin_token = $token";
        command.Parameters.AddWithValue("$token", adminToken);
        return Deserialize(await command.ExecuteScalarAsync(ct) as string);
    }

    public async Task<Tournament?> FindByScorerTokenAsync(string scorerToken, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM tournaments WHERE scorer_token = $token";
        command.Parameters.AddWithValue("$token", scorerToken);
        return Deserialize(await command.ExecuteScalarAsync(ct) as string);
    }

    public async Task<Tournament?> FindByViewerTokenAsync(string viewerToken, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM tournaments WHERE viewer_token = $token";
        command.Parameters.AddWithValue("$token", viewerToken);
        return Deserialize(await command.ExecuteScalarAsync(ct) as string);
    }

    public async Task<IReadOnlyList<Tournament>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM tournaments WHERE owner_id = $owner ORDER BY updated_at DESC";
        command.Parameters.AddWithValue("$owner", ownerId);

        var result = new List<Tournament>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Deserialize(reader.GetString(0))!);
        }

        return result;
    }

    public async Task InsertAsync(Tournament tournament, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO tournaments (id, owner_id, admin_token, scorer_token, viewer_token, json, updated_at)
                VALUES ($id, $owner, $token, $scorer, $viewer, $json, $now)
                """;
            Bind(command, tournament);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Lädt, wendet an, speichert — atomar gegenüber anderen Aufrufen.</summary>
    public async Task<Tournament> MutateAsync(Guid id, Action<Tournament> action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var tournament = await FindAsync(id, ct)
                ?? throw new Api.NotFoundException("Dieses Turnier gibt es nicht.");

            action(tournament);

            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE tournaments SET owner_id = $owner, admin_token = $token, scorer_token = $scorer, viewer_token = $viewer, json = $json, updated_at = $now
                WHERE id = $id
                """;
            Bind(command, tournament);
            await command.ExecuteNonQueryAsync(ct);
            return tournament;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tournaments WHERE id = $id";
            command.Parameters.AddWithValue("$id", id.ToString());
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // --- Sitzungen des Agenten ------------------------------------------

    public async Task<string?> FindSessionJsonAsync(string sessionId, string clientId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM sessions WHERE id = $id AND client_id = $client";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$client", clientId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// Das Gespräch zu einem Turnier: je Turnier eines. Gab es über die Zeit
    /// mehrere — etwa ein allgemeines, das später zu diesem Turnier fand —,
    /// gilt das zuletzt geführte.
    /// </summary>
    public async Task<string?> FindSessionJsonForTournamentAsync(Guid tournamentId, string clientId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT json FROM sessions WHERE tournament_id = $tournament AND client_id = $client
            ORDER BY updated_at DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$tournament", tournamentId.ToString());
        command.Parameters.AddWithValue("$client", clientId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task SaveSessionJsonAsync(string sessionId, string clientId, string json, Guid? tournamentId = null, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, client_id, tournament_id, json, updated_at) VALUES ($id, $client, $tournament, $json, $now)
            ON CONFLICT(id) DO UPDATE SET json = excluded.json, tournament_id = excluded.tournament_id, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$client", clientId);
        command.Parameters.AddWithValue("$tournament", tournamentId is { } id ? id.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Löscht ein Gespräch — nur das eigene. Sagt, ob es eines gab.</summary>
    public async Task<bool> DeleteSessionAsync(string sessionId, string clientId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id AND client_id = $client";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$client", clientId);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Mit dem Turnier gehen die Gespräche darüber — aller, die mitgeredet haben.</summary>
    public async Task DeleteSessionsOfTournamentAsync(Guid tournamentId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE tournament_id = $tournament";
        command.Parameters.AddWithValue("$tournament", tournamentId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Bind(SqliteCommand command, Tournament tournament)
    {
        command.Parameters.AddWithValue("$id", tournament.Id.ToString());
        command.Parameters.AddWithValue("$owner", tournament.OwnerId);
        command.Parameters.AddWithValue("$token", tournament.AdminToken);
        command.Parameters.AddWithValue("$scorer", tournament.ScorerToken);
        command.Parameters.AddWithValue("$viewer", tournament.ViewerToken);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(tournament.ToSnapshot(), Json));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static Tournament? Deserialize(string? json) =>
        json is null ? null : Tournament.FromSnapshot(JsonSerializer.Deserialize<TournamentSnapshot>(json, Json)!);

    /// <summary>
    /// Eine Kopie der ganzen Datenbank in eine neue Datei. VACUUM INTO liest in
    /// einer Transaktion: Die Kopie ist in sich stimmig, auch wenn nebenher
    /// jemand einträgt.
    /// </summary>
    public async Task BackupAsync(string file, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $file";
        command.Parameters.AddWithValue("$file", file);
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
