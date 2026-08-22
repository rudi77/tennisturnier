using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TennisTurnier.Api.Realtime;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Formats;
using TennisTurnier.Domain.Matches;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der Push-Kanal der öffentlichen Ansicht (ADR-0003).
///
/// Geprüft wird die ganze Kette: Ergebniseingabe → Neuaufbau der Projektion →
/// Nachricht an die Gruppe des Turniers. Ein Test, der stattdessen den Notifier
/// direkt aufriefe, ließe genau die Verdrahtung aus, an der es scheitert.
/// </summary>
public sealed class TournamentHubTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public TournamentHubTests(TennisTurnierApiFactory factory) => _factory = factory;

    /// <summary>
    /// Eine Verbindung über den Testserver. <c>HttpMessageHandlerFactory</c> ist
    /// der Weg, den In-Memory-Transport zu benutzen — ohne ihn versuchte der
    /// Client, einen echten Socket zu öffnen.
    /// </summary>
    private HubConnection Connect() =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "/hubs/tournament"),
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

    private async Task<(HttpClient Admin, Guid TournamentId)> DrawnTournamentAsync()
    {
        var aufbau = await _factory.NeuesTurnierAsync(
            "hub-admin",
            new TurnierWunsch { Anlage = "TC Hub", Teilnehmer = 4 });

        return (aufbau.Admin, aufbau.TournamentId);
    }

    [Fact]
    public async Task Ein_Zuschauer_erfaehrt_von_einem_neuen_Ergebnis()
    {
        var (admin, tournamentId) = await DrawnTournamentAsync();

        await using var connection = Connect();
        var received = new TaskCompletionSource<(Guid Id, string ETag)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<Guid, string>(
            TournamentHub.ProjectionChanged,
            (id, etag) => received.TrySetResult((id, etag)));

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", tournamentId);

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.Single().Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        var push = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(tournamentId, push.Id);

        // Der Push trägt den ETag, damit der Client sofort weiß, ob er den Stand
        // schon hat — der Abruf danach ist derselbe wie beim Polling.
        var current = await _factory.CreateClient().GetAsync($"/public/tournaments/{tournamentId}");
        Assert.Equal(push.ETag, current.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Wer_ein_anderes_Turnier_abonniert_hat_bekommt_nichts()
    {
        // Ohne Gruppen ginge jede Ergebnismeldung an alle Verbindungen — auch an
        // die eines ganz anderen Vereins.
        var (admin, tournamentId) = await DrawnTournamentAsync();

        await using var connection = Connect();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid, string>(TournamentHub.ProjectionChanged, (_, _) => received.TrySetResult());

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", Guid.NewGuid());

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.Single().Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        await Assert.ThrowsAsync<TimeoutException>(
            () => received.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Eine_Verbindung_verfolgt_nicht_beliebig_viele_Turniere()
    {
        // Der Hub ist ohne Anmeldung erreichbar. Ohne Grenze legte eine einzige
        // Verbindung beliebig viele Gruppen an — und der Speicher des Servers
        // hinge am Wohlwollen des Aufrufers.
        await using var connection = Connect();
        await connection.StartAsync();

        for (var i = 0; i < 16; i++)
        {
            await connection.InvokeAsync("Subscribe", Guid.NewGuid());
        }

        // Dasselbe Turnier noch einmal geht immer — es entsteht keine Gruppe dazu.
        var ersteId = Guid.NewGuid();
        await connection.InvokeAsync("Unsubscribe", ersteId);

        var fehler = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("Subscribe", Guid.NewGuid()));

        Assert.Contains("höchstens 16 Turniere", fehler.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wer_sich_abmeldet_bekommt_nichts_mehr()
    {
        var (admin, tournamentId) = await DrawnTournamentAsync();

        await using var connection = Connect();
        var eingegangen = 0;

        connection.On<Guid, string>(
            TournamentHub.ProjectionChanged,
            (_, _) => Interlocked.Increment(ref eingegangen));

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", tournamentId);
        await connection.InvokeAsync("Unsubscribe", tournamentId);

        var phases = await admin.GetFromJsonAsync<List<PhaseDetail>>(
            $"/api/tournaments/{tournamentId}/phases", Json);
        var match = phases!.Single().Matches.First(m => m.Status == MatchStatus.Ready);

        await admin.PutAsJsonAsync(
            $"/api/matches/{match.Id}/result",
            new RecordResultRequest(MatchOutcome.Normal, [new SetScore(6, 4), new SetScore(6, 2)]),
            Json);

        // Kurz warten reicht: käme noch etwas, käme es in derselben Zeitspanne
        // wie im Test darüber.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, eingegangen);
    }
}
