using System.Text;
using Matchday.Server.Api;
using Matchday.Server.Live;
using Matchday.Server.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, turnier.ViewerToken, abbruch.Token);

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

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, turnier.ViewerToken, CancellationToken.None);
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

        var lauf = Endpoints.Live(http, actions, hub, turnier.Id, turnier.ViewerToken, abbruch.Token);

        await Warten(mitschrift, ": keepalive");

        await abbruch.CancelAsync();
        await lauf;
    }

    [Fact]
    public async Task Ein_erneuerter_Mitschau_Link_beendet_die_Mitschau_mit_dem_alten()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss();

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, turnier.ViewerToken, CancellationToken.None);
        await Warten(mitschrift, "event: view");

        await _a.Actions.RotateViewerTokenAsync(_a.Rudi, turnier.Id, CancellationToken.None);

        // Kein Abbruch nötig: Der alte Link trägt nicht mehr, der Strom endet von selbst.
        await lauf;

        Assert.Contains("event: revoked", Text(mitschrift));
        Assert.Equal(1, Zaehlen(mitschrift, "event: view"));
        Assert.Equal(0, _a.Live.SubscriberCount(turnier.Id));
    }

    [Fact]
    public async Task Verwaltung_und_Eintragen_schauen_mit_ihrem_Link_mit_und_bleiben_beim_neuen_Mitschau_Link()
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (verwaltung, mitschriftVerwaltung) = Anschluss();
        var (helfer, mitschriftHelfer) = Anschluss();
        using var abbruch = new CancellationTokenSource();

        var laufVerwaltung = Endpoints.Live(verwaltung, _a.Actions, _a.Live, turnier.Id, turnier.AdminToken, abbruch.Token);
        var laufHelfer = Endpoints.Live(helfer, _a.Actions, _a.Live, turnier.Id, turnier.ScorerToken, abbruch.Token);
        await Warten(mitschriftVerwaltung, "event: view");
        await Warten(mitschriftHelfer, "event: view");

        await _a.Actions.RotateViewerTokenAsync(_a.Rudi, turnier.Id, CancellationToken.None);
        await Warten(mitschriftVerwaltung, "event: view", anzahl: 2);
        await Warten(mitschriftHelfer, "event: view", anzahl: 2);

        await abbruch.CancelAsync();
        await Task.WhenAll(laufVerwaltung, laufHelfer);

        Assert.Equal(2, Zaehlen(mitschriftVerwaltung, "event: view"));
        Assert.Equal(2, Zaehlen(mitschriftHelfer, "event: view"));
        Assert.DoesNotContain("revoked", Text(mitschriftVerwaltung) + Text(mitschriftHelfer));
    }

    [Fact]
    public async Task Die_Turnierleitung_schaut_ohne_Link_mit_ihrem_Browser_mit()
    {
        // Wer ein Turnier im Gespräch anlegt, hat oft noch kein Token im
        // Browser — live soll es trotzdem sein.
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss(_a.Rudi.ClientId);
        using var abbruch = new CancellationTokenSource();

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, null, abbruch.Token);
        await Warten(mitschrift, "event: view");

        await _a.Actions.RotateViewerTokenAsync(_a.Rudi, turnier.Id, CancellationToken.None);
        await Warten(mitschrift, "event: view", anzahl: 2);

        await abbruch.CancelAsync();
        await lauf;

        Assert.DoesNotContain("revoked", Text(mitschrift));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("geraten", null)]
    [InlineData(null, "browser-fremd")]
    public async Task Ohne_gueltigen_Link_gibt_es_keine_Mitschau(string? schluessel, string? browser)
    {
        var turnier = await _a.Actions.CreateAsync(_a.Rudi, new CreateTournamentRequest("Sommercup"), CancellationToken.None);
        var (http, mitschrift) = Anschluss(browser);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, schluessel, CancellationToken.None));

        Assert.Empty(Text(mitschrift));
        Assert.Equal(0, _a.Live.SubscriberCount(turnier.Id));
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

        var lauf = Endpoints.Live(http, _a.Actions, _a.Live, turnier.Id, turnier.ViewerToken, abbruch.Token);
        await Warten(mitschrift, "event: view");
        Assert.Equal(1, _a.Live.SubscriberCount(turnier.Id));

        await abbruch.CancelAsync();
        await lauf;

        Assert.Equal(0, _a.Live.SubscriberCount(turnier.Id));
    }

    // --- Werkzeug ----------------------------------------------------------

    /// <summary>Ein Anschluss ohne Netz und ohne Anmeldung: die Antwort läuft in einen Puffer.</summary>
    private static (HttpContext Http, MemoryStream Mitschrift) Anschluss(string? browser = null)
    {
        var mitschrift = new MemoryStream();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddOptions().Configure<AuthOptions>(_ => { }).BuildServiceProvider(),
        };
        http.Response.Body = mitschrift;

        if (browser is not null)
        {
            http.Request.Headers.Cookie = $"{Endpoints.ClientCookie}={browser}";
        }

        return (http, mitschrift);
    }

    private static string Text(MemoryStream mitschrift) => Encoding.UTF8.GetString(mitschrift.ToArray());

    private static int Zaehlen(MemoryStream mitschrift, string gesucht) =>
        Text(mitschrift).Split(gesucht).Length - 1;

    /// <summary>Wartet, bis der Puffer den Text (so oft) enthält — oder die Geduld endet.</summary>
    private static async Task Warten(MemoryStream mitschrift, string gesucht, int anzahl = 1)
    {
        for (var versuch = 0; versuch < 500; versuch++)
        {
            if (Zaehlen(mitschrift, gesucht) >= anzahl)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"„{gesucht}“ kam nicht an. Angekommen ist: {Text(mitschrift)}");
    }
}
