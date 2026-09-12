using System.Text;
using Matchday.Server.Api;
using Matchday.Server.Live;
using Microsoft.AspNetCore.Http;

namespace Matchday.Server.Tests;

/// <summary>
/// Die Mitschau: die erste Sicht, jede Änderung, das Lebenszeichen, und was
/// passiert, wenn das Turnier verschwindet oder der Browser die Seite zumacht.
/// </summary>
public sealed class LiveTests : IDisposable
{
    private readonly Aufbau _a = new();

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task Die_Mitschau_schickt_zuerst_die_Sicht_und_dann_jede_Aenderung()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss();
        using var abbruch = new CancellationTokenSource();

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, abbruch.Token);

        await Warten(mitschrift, "event: view");

        // Ein Teilnehmer kommt dazu — die Mitschau bekommt die ganze Sicht.
        await _a.Actions.AddParticipantsAsync(_a.Rudi, turnier.Id, ["Rudi"], CancellationToken.None);
        await Warten(mitschrift, "Rudi");

        await abbruch.CancelAsync();
        await lauf;

        Assert.Equal(2, Zaehlen(mitschrift, "event: view"));
    }

    [Fact]
    public async Task Ein_geloeschtes_Turnier_beendet_die_Mitschau()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss();

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, CancellationToken.None);
        await Warten(mitschrift, "event: view");

        await _a.Actions.DeleteAsync(_a.Rudi, turnier.Id, CancellationToken.None);

        // Kein Abbruch nötig: das Ende des Turniers ist das Ende der Mitschau.
        await lauf;

        Assert.Contains("event: deleted", Text(mitschrift));
        Assert.Contains(turnier.Id.ToString(), Text(mitschrift));
    }

    [Fact]
    public async Task Ohne_Aenderung_kommt_ein_Lebenszeichen()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var hub = new LiveHub { KeepAlive = TimeSpan.FromMilliseconds(20) };
        var actions = new TournamentActions(_a.Store, hub, TimeProvider.System);
        var (http, mitschrift) = Anschluss();
        using var abbruch = new CancellationTokenSource();

        var lauf = Endpoints.Live(http, actions, hub, turnier.Id, abbruch.Token);

        await Warten(mitschrift, ": keepalive");

        await abbruch.CancelAsync();
        await lauf;
    }

    [Fact]
    public void Wer_nicht_mitschaut_wird_nicht_gezaehlt()
    {
        Assert.Equal(0, _a.Live.SubscriberCount(Guid.NewGuid()));
    }

    [Fact]
    public async Task Eine_beendete_Mitschau_wird_nicht_mehr_gezaehlt()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss();
        using var abbruch = new CancellationTokenSource();

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, abbruch.Token);
        await Warten(mitschrift, "event: view");
        Assert.Equal(1, _a.Live.SubscriberCount(turnier.Id));

        await abbruch.CancelAsync();
        await lauf;

        Assert.Equal(0, _a.Live.SubscriberCount(turnier.Id));
    }

    // --- Werkzeug ----------------------------------------------------------

    /// <summary>Ein Anschluss ohne Netz: die Antwort läuft in einen Puffer.</summary>
    private static (HttpContext Http, MemoryStream Mitschrift) Anschluss()
    {
        var mitschrift = new MemoryStream();
        var http = new DefaultHttpContext();
        http.Response.Body = mitschrift;

        return (http, mitschrift);
    }

    private static string Text(MemoryStream mitschrift) => Encoding.UTF8.GetString(mitschrift.ToArray());

    private static int Zaehlen(MemoryStream mitschrift, string gesucht) =>
        Text(mitschrift).Split(gesucht).Length - 1;

    /// <summary>Wartet, bis der Puffer den Text enthält — oder die Geduld endet.</summary>
    private static async Task Warten(MemoryStream mitschrift, string gesucht)
    {
        for (var versuch = 0; versuch < 500; versuch++)
        {
            if (Text(mitschrift).Contains(gesucht, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"„{gesucht}“ kam nicht an. Angekommen ist: {Text(mitschrift)}");
    }
}
