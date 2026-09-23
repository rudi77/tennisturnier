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

    [Fact]
    public void Ohne_Freigabeliste_darf_jedes_Konto_herein()
    {
        // So stand es in ADR-0019, und so bleibt es, solange niemand eine
        // Liste setzt — auch ein Konto ganz ohne Adresse.
        var http = Anfrage(Verlangt(), Angemeldet("1"));

        Assert.Equal("google:1", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Ein_freigegebenes_Konto_kommt_herein_gleich_wie_geschrieben()
    {
        var http = Anfrage(Freigegeben("anna@example.org"), Angemeldet("1", ("email", "Anna@Example.org"), ("email_verified", "true")));

        Assert.Equal("google:1", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Die_Adresse_wird_auch_unter_dem_abgebildeten_Namen_gefunden()
    {
        // ASP.NET bildet „email" je nach Einstellung auf ClaimTypes.Email ab —
        // wie beim Subjekt müssen beide Wege tragen.
        var http = Anfrage(Freigegeben("anna@example.org"), Angemeldet("1", (ClaimTypes.Email, "anna@example.org"), ("email_verified", "True")));

        Assert.Equal("google:1", Endpoints.ActorOf(http).ClientId);
    }

    [Fact]
    public void Ein_fremdes_Konto_bekommt_403_mit_seiner_Adresse()
    {
        // 403 und nicht 401: Eine neue Anmeldung mit demselben Konto änderte
        // nichts, die Oberfläche soll also nicht zurück auf die Anmeldung.
        var http = Anfrage(Freigegeben("anna@example.org"), Angemeldet("2", ("email", "tom@example.org"), ("email_verified", "true")));

        var fehler = Assert.Throws<ForbiddenException>(() => Endpoints.ActorOf(http));

        Assert.Contains("tom@example.org", fehler.Message);
    }

    [Fact]
    public void Eine_unbestaetigte_Adresse_zaehlt_nicht()
    {
        var http = Anfrage(Freigegeben("anna@example.org"), Angemeldet("3", ("email", "anna@example.org"), ("email_verified", "false")));

        Assert.Throws<ForbiddenException>(() => Endpoints.ActorOf(http));
    }

    [Fact]
    public void Ein_Konto_ohne_Adresse_kommt_an_einer_Liste_nicht_vorbei()
    {
        var http = Anfrage(Freigegeben("anna@example.org"), Angemeldet("4"));

        var fehler = Assert.Throws<ForbiddenException>(() => Endpoints.RequireLogin(http));

        Assert.Contains("ohne E-Mail-Adresse", fehler.Message);
    }

    [Theory]
    [InlineData("anna@example.org, tom@example.org", "tom@example.org", true)]
    [InlineData("anna@example.org;tom@example.org", "tom@example.org", true)]
    [InlineData("anna@example.org\ntom@example.org", " tom@example.org ", true)]
    [InlineData(" , ; ", "wer@auch.immer", true)]
    [InlineData("anna@example.org", "", false)]
    [InlineData("anna@example.org", null, false)]
    [InlineData("anna@example.org", "anna@example.org.evil", false)]
    public void Die_Liste_versteht_die_ueblichen_Trenner(string liste, string? email, bool erwartet)
    {
        Assert.Equal(erwartet, new AuthOptions { AllowedEmails = liste }.Allows(email, verified: true));
    }

    private static AuthOptions Verlangt() => new() { Required = true, GoogleClientId = "matchday.apps.googleusercontent.com" };

    private static AuthOptions Freigegeben(string liste)
    {
        var optionen = Verlangt();
        optionen.AllowedEmails = liste;
        return optionen;
    }

    private static ClaimsPrincipal Angemeldet(string subjekt, params (string Typ, string Wert)[] weitere) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subjekt), .. weitere.Select(w => new Claim(w.Typ, w.Wert))],
            authenticationType: "Test"));

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
