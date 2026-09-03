using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TennisTurnier.Application.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Was die Benutzerauflösung aus einem Token macht, das nicht so aussieht wie
/// erwartet (ADR-0007).
///
/// Ein Token ist kein Vertrag über seinen Inhalt: Entra ID legt keine E-Mail
/// hinein, manche Aussteller kein <c>name</c>, und ein falsch eingerichtetes
/// Verfahren gar kein <c>sub</c>. Jeder dieser Fälle muss eine Antwort ergeben
/// — ein 500 an dieser Stelle träfe jeden Aufruf des Betroffenen, nicht nur
/// einen.
/// </summary>
public sealed class BenutzeraufloesungTests : IClassFixture<TennisTurnierApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TennisTurnierApiFactory _factory;

    public BenutzeraufloesungTests(TennisTurnierApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Ein_Token_ohne_Subject_bleibt_anonym()
    {
        // Ohne sub gibt es keine stabile Kennung, an der Rollen und Meldungen
        // hängen könnten. Der Aufruf läuft weiter — als der von niemandem.
        var client = _factory.CreateClientAs(
            $"ohne-sub-{Guid.NewGuid():N}", email: null, ohneClaims: "sub");

        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.NoContent, me.StatusCode);

        // Und schreiben darf er nichts: der Zustand „angemeldet, aber ohne
        // Konto" darf nicht mehr können als „nicht angemeldet".
        var turniere = await client.GetAsync("/api/tournaments");
        Assert.Equal(HttpStatusCode.OK, turniere.StatusCode);
    }

    [Fact]
    public async Task Ohne_iss_Claim_zaehlt_der_Aussteller_des_Subjects()
    {
        // Zwei Aufrufe desselben Benutzers, einmal mit und einmal ohne
        // iss-Claim: es muss dasselbe Konto herauskommen, sonst bekäme derselbe
        // Mensch beim nächsten Anmelden ein zweites.
        var subject = $"ohne-iss-{Guid.NewGuid():N}";

        var mit = await _factory.CreateClientAs(subject)
            .GetFromJsonAsync<MeResponse>("/api/me", Json);

        var ohne = await _factory.CreateClientAs(subject, email: null, ohneClaims: "iss")
            .GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(mit);
        Assert.NotNull(ohne);
        Assert.Equal(mit.UserId, ohne.UserId);
    }

    [Fact]
    public async Task Ohne_name_Claim_zaehlt_der_Anmeldename()
    {
        // Nicht jeder Aussteller legt „name" hinein. Dann steht der Anmeldename
        // da — besser als gar nichts, und besser als ein erfundener.
        var subject = $"ohne-name-{Guid.NewGuid():N}";
        var client = _factory.CreateClientAs(
            subject, email: "ohne.name@example.invalid", ohneClaims: "name");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Equal($"{subject}@kennung", me.DisplayName);
        Assert.Equal("ohne.name@example.invalid", me.Email);
    }

    [Fact]
    public async Task Eine_unbestaetigte_Adresse_wird_nicht_uebernommen()
    {
        // Die Adresse ist der Schlüssel, an dem Einladungen, der erste
        // Systemadministrator und die Übernahme eines importierten Spielers
        // hängen. Ein Aussteller mit offener Selbstregistrierung lässt sie frei
        // wählen — übernähme die Anwendung sie ungeprüft, erbte der Schnellere
        // alles, was für den Inhaber hinterlegt wurde.
        var client = _factory.CreateClientAs(
            $"unbestaetigt-{Guid.NewGuid():N}",
            email: "fremde.adresse@example.invalid",
            emailBestaetigt: false);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Null(me.Email);
    }

    [Fact]
    public async Task Ohne_email_verified_Claim_zaehlt_die_Adresse_als_unbestaetigt()
    {
        // „Der Aussteller sagt nichts dazu" ist keine Bestätigung. Ein
        // fehlender Claim muss deshalb wie ein verneinter wirken — sonst
        // genügte es, einen Aussteller ohne diesen Claim davorzuhängen.
        var client = _factory.CreateClientAs(
            $"ohne-verified-{Guid.NewGuid():N}",
            email: "schweigsam@example.invalid",
            ohneClaims: "email_verified");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Null(me.Email);
    }

    [Fact]
    public async Task Ein_Aussteller_ohne_den_Claim_laesst_sich_ausdruecklich_zulassen()
    {
        // Es gibt Verbünde, die den Claim nicht ausstellen und trotzdem nur
        // bestätigte Adressen herausgeben — ein Firmenverzeichnis ohne
        // Selbstregistrierung etwa. Diese Aussage kann nur der Betreiber
        // treffen, und deshalb ist sie ein Schalter und keine stille Annahme.
        using var factory = new TennisTurnierApiFactory([], trustUnverifiedEmail: true);

        var client = factory.CreateClientAs(
            $"vertraut-{Guid.NewGuid():N}",
            email: "vertraut@example.invalid",
            ohneClaims: "email_verified");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Equal("vertraut@example.invalid", me.Email);
    }

    [Fact]
    public async Task Ganz_ohne_Namen_bleibt_der_Anzeigename_leer()
    {
        // Entra ID stellt Token ohne beides aus. Eine leere Anzeige ist dann
        // richtig — die Oberfläche zeigt die Adresse.
        var client = _factory.CreateClientAs(
            $"namenlos-{Guid.NewGuid():N}",
            email: "namenlos@example.invalid",
            ohneClaims: "name,preferred_username");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Null(me.DisplayName);
    }
}
