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
    public async Task Ohne_Namen_im_Token_bleibt_der_Anzeigename_leer()
    {
        // Lieber kein Name als ein erfundener: die Anzeige fällt dann auf die
        // Adresse zurück, und niemand steht unter einer Kennung da, die er nie
        // gewählt hat.
        var client = _factory.CreateClientAs(
            $"ohne-name-{Guid.NewGuid():N}",
            email: "ohne.name@example.invalid",
            ohneClaims: "name");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.NotNull(me);
        Assert.Null(me.DisplayName);
        Assert.Equal("ohne.name@example.invalid", me.Email);
    }
}
