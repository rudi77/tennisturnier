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
                INSERT INTO tournaments (id, owner_id, admin_token, json, updated_at)
                VALUES ($id, $owner, $token, $json, $now)
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
                UPDATE tournaments SET owner_id = $owner, admin_token = $token, json = $json, updated_at = $now
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

    public async Task SaveSessionJsonAsync(string sessionId, string clientId, string json, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (id, client_id, json, updated_at) VALUES ($id, $client, $json, $now)
            ON CONFLICT(id) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$client", clientId);
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Bind(SqliteCommand command, Tournament tournament)
    {
        command.Parameters.AddWithValue("$id", tournament.Id.ToString());
        command.Parameters.AddWithValue("$owner", tournament.OwnerId);
        command.Parameters.AddWithValue("$token", tournament.AdminToken);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(tournament.ToSnapshot(), Json));
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static Tournament? Deserialize(string? json) =>
        json is null ? null : Tournament.FromSnapshot(JsonSerializer.Deserialize<TournamentSnapshot>(json, Json)!);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
