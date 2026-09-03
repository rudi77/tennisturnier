using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TennisTurnier.Adapters.Identity.Oidc;
using TennisTurnier.Application.Ports;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Die Verdrahtung des Identity Providers (ADR-0007).
///
/// Sie ist Konfiguration und kein Code — genau deshalb steht sie hier unter
/// Prüfung: eine Vertippung in dieser Datei fällt sonst erst auf, wenn sich
/// jemand nicht mehr anmelden kann, und dann in Produktion. Geprüft wird auch
/// das Gegenteil: ohne Authority startet die Anwendung rein öffentlich, statt
/// mit einer Prüfung, die nichts prüft.
/// </summary>
public sealed class IdentityRegistrationTests
{
    private const string Authority = "https://login.example.invalid/realms/tennisturnier";

    private static ServiceProvider Verdrahten(OidcOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOidcIdentity(options);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Ohne_Authority_gibt_es_ein_Verfahren_das_niemanden_ausweist()
    {
        // Ausgestellt wird hier nichts — es gibt keinen Aussteller. Registriert
        // ist trotzdem eines, weil die Autorisierung nach einem fragt, sobald
        // ein Endpunkt einen Ausweis verlangt. Ohne dieses Schema endete das in
        // „No authenticationScheme was specified": einer 500 auf einen Aufruf,
        // dessen richtige Antwort 401 ist.
        using var provider = Verdrahten(new OidcOptions());

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        var schema = Assert.Single(options.Schemes);

        Assert.Equal("OhneAussteller", schema.Name);
        Assert.Equal("OhneAussteller", options.DefaultScheme);

        // Und es weist niemanden aus: kein JWT-Verfahren, das etwas zu prüfen
        // vorgäbe.
        Assert.DoesNotContain(options.Schemes, s => s.Name == JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void Die_Benutzeraufloesung_wird_immer_registriert()
    {
        // Auch ohne Aussteller: die Pipeline behält UseAuthentication und
        // UseUserResolution, und Tests hängen dort ihr eigenes Schema ein.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOidcIdentity(new OidcOptions());

        Assert.Contains(services, d => d.ServiceType == typeof(UserResolutionMiddleware));
        Assert.Contains(services, d => d.ServiceType == typeof(IUserContext));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Und er ist im Bereich derselbe — die Auflösung schreibt ihr Ergebnis
        // hinein, der Anwendungsfall liest es daraus.
        var kontext = scope.ServiceProvider.GetRequiredService<IUserContext>();
        Assert.NotNull(kontext);
        Assert.Same(kontext, scope.ServiceProvider.GetRequiredService<IUserContext>());
    }

    [Fact]
    public void Mit_Authority_prueft_das_Bearer_Verfahren_das_Token()
    {
        using var provider = Verdrahten(new OidcOptions
        {
            Authority = Authority,
            Audience = "tennisturnier-api",
            RequireHttpsMetadata = true,
        });

        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, authentication.DefaultScheme);

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(Authority, jwt.Authority);
        Assert.Equal("tennisturnier-api", jwt.Audience);
        Assert.True(jwt.RequireHttpsMetadata);

        // Die Claims behalten ihre Namen aus dem Token: „sub" bleibt „sub" und
        // wird nicht auf eine WS-Federation-URI abgebildet.
        Assert.False(jwt.MapInboundClaims);
        Assert.Equal("name", jwt.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", jwt.TokenValidationParameters.RoleClaimType);

        Assert.True(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.True(jwt.TokenValidationParameters.ValidateAudience);
        Assert.True(jwt.TokenValidationParameters.ValidateLifetime);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuerSigningKey);

        // Eine halbe Minute deckt Uhrenversatz ab; die Vorgabe von fünf Minuten
        // wäre für einen Turniertag großzügig.
        Assert.Equal(TimeSpan.FromSeconds(30), jwt.TokenValidationParameters.ClockSkew);
    }

    [Fact]
    public void Ohne_Audience_wird_der_Empfaenger_nicht_geprueft()
    {
        // Für einen Aussteller ohne feste Audience — eine Prüfung gegen die
        // leere Zeichenkette wiese jedes seiner Token ab. Dass das eine
        // ausdrückliche Entscheidung ist und kein vergessenes Feld, stellt die
        // Composition Root beim Start sicher (siehe OffenerBetriebTests).
        using var provider = Verdrahten(
            new OidcOptions { Authority = Authority, Audience = "  ", RequireAudience = false });

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(jwt.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void Http_Metadaten_sind_ausdruecklich_abschaltbar()
    {
        // Nur für die lokale Entwicklung gegen Keycloak über HTTP.
        using var provider = Verdrahten(new OidcOptions
        {
            Authority = "http://localhost:8080/realms/tennisturnier",
            RequireHttpsMetadata = false,
        });

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(jwt.RequireHttpsMetadata);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(Authority, true)]
    public void Konfiguriert_ist_sie_genau_mit_einer_Authority(string authority, bool erwartet)
    {
        Assert.Equal(erwartet, new OidcOptions { Authority = authority }.IsConfigured);
    }
}
