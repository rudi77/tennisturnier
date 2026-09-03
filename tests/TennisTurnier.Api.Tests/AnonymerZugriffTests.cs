using System.Net;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die zweite Verteidigungslinie: was ein Aufruf ohne Anmeldung erreicht.
///
/// Die eigentliche Grenze ist und bleibt die Rechteprüfung im Anwendungsfall —
/// sie entscheidet, wer was darf, und antwortet mit 404 statt 403 (ADR-0004).
/// Sie hielt an jeder geprüften Stelle. Sie war aber die einzige: die
/// Turnier-, Match-, Rollen-, Vorlagen- und Spielerendpunkte trugen kein
/// <c>RequireAuthorization</c>, und ein anonymer Aufruf lief bis in den Dienst
/// und in die Datenbank.
///
/// ADR-0004 verlangt die Prüfung am Endpunkt ausdrücklich als zweite Linie.
/// Sie entscheidet nichts, was die erste entscheidet; sie sorgt dafür, dass
/// ein künftig vergessenes <c>Require()</c> nicht sofort für jeden offensteht.
/// </summary>
public sealed class AnonymerZugriffTests : IClassFixture<TennisTurnierApiFactory>
{
    private readonly TennisTurnierApiFactory _factory;

    public AnonymerZugriffTests(TennisTurnierApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> Verschlossen() => new()
    {
        { "GET", "/api/tournaments" },
        { "POST", "/api/tournaments" },
        { "GET", "/api/format-templates" },
        { "GET", "/api/players?q=meier" },
        { "POST", "/api/participants" },
    };

    [Theory]
    [MemberData(nameof(Verschlossen))]
    public async Task Ohne_Anmeldung_endet_es_am_Endpunkt(string methode, string pfad)
    {
        var anonym = _factory.CreateClient();

        var antwort = await anonym.SendAsync(
            new HttpRequestMessage(new HttpMethod(methode), pfad));

        Assert.Equal(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    public static TheoryData<string> Offen() =>
    [
        "/health",
        "/config.js",
        "/api/me",
        "/public/tournaments/00000000-0000-0000-0000-000000000000",
    ];

    [Theory]
    [MemberData(nameof(Offen))]
    public async Task Was_offen_stehen_soll_steht_weiter_offen(string pfad)
    {
        // Kein 401: diese vier sind der öffentliche Teil der Anwendung. Was sie
        // antworten, ist hier nicht der Gegenstand — nur, dass sie überhaupt
        // antworten. Die Live-Ansicht eines Turniers, das es nicht gibt, sagt
        // 404, und das ist die richtige Auskunft für jeden.
        var antwort = await _factory.CreateClient().GetAsync(pfad);

        Assert.NotEqual(HttpStatusCode.Unauthorized, antwort.StatusCode);
    }

    /// <summary>
    /// Eine Instanz ganz ohne Aussteller antwortet 401 und nicht 500.
    ///
    /// Ohne Authority registriert der Identity-Adapter kein Verfahren, mit dem
    /// sich jemand ausweisen könnte. Die Autorisierung fragt trotzdem nach
    /// einem, sobald ein Endpunkt einen Ausweis verlangt — und fand keines: der
    /// Aufruf endete in „No authenticationScheme was specified". Die README
    /// verspricht an dieser Stelle „nur die öffentlichen Endpunkte", und das
    /// heißt 401 auf die anderen, nicht 500.
    ///
    /// <c>testSchema: false</c> ist hier der Kern und keine Beiläufigkeit: mit
    /// Testschema gäbe es ein Verfahren, und die Lücke bliebe ungeprüft.
    /// </summary>
    [Fact]
    public async Task Ohne_jeden_Aussteller_bleibt_es_bei_401()
    {
        using var fabrik = new TennisTurnierApiFactory([], testSchema: false);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fabrik.CreateClient().GetAsync("/api/tournaments")).StatusCode);

        // Auch die Wege, die schon vorher einen Ausweis verlangten und deshalb
        // in einer 500 endeten.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fabrik.CreateClient().GetAsync("/api/me/connections")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fabrik.CreateClient().GetAsync($"/api/join/{Guid.NewGuid():N}")).StatusCode);

        // Und der öffentliche Teil bleibt erreichbar — das ist es, was die
        // README verspricht.
        Assert.Equal(
            HttpStatusCode.OK,
            (await fabrik.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Ein_Ausweis_ohne_Subject_kommt_am_Endpunkt_vorbei()
    {
        // Die Feinheit, die bleiben muss: die Fallback-Richtlinie fragt nach
        // einem angemeldeten Aufrufer, nicht nach einem aufgelösten Konto. Ein
        // Token ohne `sub` ist angemeldet und kommt durch — dahinter ist er
        // niemand, und die Rechteprüfung lässt ihn nichts tun.
        //
        // Das ist Absicht: die zweite Linie soll die erste nicht ersetzen. Wer
        // hier 401 antwortete, verschöbe die Entscheidung an eine Stelle, die
        // die Rechte gar nicht kennt.
        var client = _factory.CreateClientAs(
            $"ohne-sub-{Guid.NewGuid():N}", email: null, ohneClaims: "sub");

        var turniere = await client.GetAsync("/api/tournaments");

        Assert.Equal(HttpStatusCode.OK, turniere.StatusCode);
    }
}
