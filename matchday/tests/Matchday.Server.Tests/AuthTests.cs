using System.Security.Claims;
using Matchday.Server.Api;
using Matchday.Server.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Matchday.Server.Tests;

/// <summary>
/// Wer handeln darf. Der Schalter steht in der Konfiguration; hier steht, was
/// er tatsächlich bewirkt — in beiden Stellungen (ADR-0019).
/// </summary>
public sealed class AuthTests
{
    [Fact]
    public void Ohne_Anmeldung_zaehlt_weiter_die_Browserkennung()
    {
        var http = Anfrage(new AuthOptions { Required = false });
        http.Request.Headers[Endpoints.ClientHeader] = "browser-rudi";

        Assert.Equal("browser-rudi", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Ohne_Anmeldung_und_ohne_Kopfzeile_bleibt_es_beim_alten_Fehler()
    {
        var http = Anfrage(new AuthOptions { Required = false });

        Assert.Throws<ForbiddenException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Mit_Anmeldung_zaehlt_das_Konto_und_nicht_die_Kopfzeile()
    {
        // Die Kopfzeile ist gesetzt und soll trotzdem nicht gelten: Sie kann
        // jeder schicken, das Token nicht.
        var http = Anfrage(Verlangt(), Angemeldet("1234567890"));
        http.Request.Headers[Endpoints.ClientHeader] = "browser-fremd";

        Assert.Equal("google:1234567890", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Mit_Anmeldung_aber_ohne_Token_ist_es_401_und_nicht_403()
    {
        var http = Anfrage(Verlangt());
        http.Request.Headers[Endpoints.ClientHeader] = "browser-rudi";

        Assert.Throws<UnauthorizedException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Ein_Token_ohne_Subjekt_gilt_nicht()
    {
        // Angemeldet, aber ohne die Angabe, wer da angemeldet ist — daraus
        // ließe sich kein Eigentümer bilden.
        var identity = new ClaimsIdentity(authenticationType: "Test");
        var http = Anfrage(Verlangt(), new ClaimsPrincipal(identity));

        Assert.Throws<UnauthorizedException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Angaben_ohne_Authentifizierung_gelten_nicht()
    {
        // Ein ClaimsIdentity ohne authenticationType trägt Angaben, ist aber
        // nicht angemeldet. Würde hier nur auf das Subjekt geschaut, käme
        // jeder mit einem selbst gesetzten sub durch.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1234567890")]);
        var http = Anfrage(Verlangt(), new ClaimsPrincipal(identity));

        Assert.Throws<UnauthorizedException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Ganz_ohne_Identitaet_gilt_erst_recht_nichts()
    {
        var http = Anfrage(Verlangt(), new ClaimsPrincipal());

        Assert.Throws<UnauthorizedException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Das_Subjekt_wird_auch_aus_sub_gelesen()
    {
        // Je nach Einstellung bildet ASP.NET sub auf NameIdentifier ab — oder
        // eben nicht. Beide Wege müssen tragen.
        var identity = new ClaimsIdentity([new Claim("sub", "42")], authenticationType: "Test");
        var http = Anfrage(Verlangt(), new ClaimsPrincipal(identity));

        Assert.Equal("google:42", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Der_Verwalterlink_verlangt_ohne_Schalter_nichts()
    {
        Endpoints.RequireLogin(Anfrage(new AuthOptions { Required = false }));
    }

    [Fact]
    public void Der_Verwalterlink_verlangt_mit_Schalter_eine_Anmeldung()
    {
        Assert.Throws<UnauthorizedException>(() => Endpoints.RequireLogin(Anfrage(Verlangt())));
        Endpoints.RequireLogin(Anfrage(Verlangt(), Angemeldet("7")));
    }

    [Fact]
    public void Das_Verwaltertoken_bleibt_neben_dem_Konto_bestehen()
    {
        var http = Anfrage(Verlangt(), Angemeldet("1"));
        http.Request.Headers[Endpoints.AdminHeader] = "geheim";

        Assert.Equal("geheim", Endpoints.ActorOf(http).AdminToken);
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "", true)]
    [InlineData(true, "client-id", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "   ", false)]
    public void Verlangen_ohne_Client_Id_ist_keine_gueltige_Einstellung(bool verlangt, string? clientId, bool erwartet)
    {
        var optionen = new AuthOptions { Required = verlangt, GoogleClientId = clientId };

        Assert.Equal(erwartet, optionen.IsConfigured);
    }

    private static AuthOptions Verlangt() => new() { Required = true, GoogleClientId = "matchday.apps.googleusercontent.com" };

    private static ClaimsPrincipal Angemeldet(string subjekt) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subjekt)], authenticationType: "Test"));

    private static HttpContext Anfrage(AuthOptions optionen, ClaimsPrincipal? benutzer = null)
    {
        var dienste = new ServiceCollection();
        dienste.AddSingleton<IOptions<AuthOptions>>(Options.Create(optionen));

        return new DefaultHttpContext
        {
            RequestServices = dienste.BuildServiceProvider(),
            User = benutzer ?? new ClaimsPrincipal(new ClaimsIdentity()),
        };
    }
}
