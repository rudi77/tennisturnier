using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Matchday.Server.Tests;

/// <summary>
/// Das Google-Token wird einmal eingelöst, danach trägt das Cookie (ADR-0025).
/// Google selbst ist hier nicht zu haben: Die Token signiert der Test mit einem
/// eigenen Schlüssel, und nur dieser eine Schlüssel wird anerkannt — geprüft
/// werden Aussteller, Audience und Ablauf wie im Betrieb.
/// </summary>
public sealed class SitzungTests : IDisposable
{
    private const string ClientId = "matchday.apps.googleusercontent.com";

    private static readonly SymmetricSecurityKey Schluessel = new(new byte[32] { 7, 1, 4, 2, 8, 5, 7, 1, 4, 2, 8, 5, 7, 1, 4, 2, 8, 5, 7, 1, 4, 2, 8, 5, 7, 1, 4, 2, 8, 5, 7, 1 });

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"matchday-sitzung-{Guid.NewGuid():N}.db");
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"matchday-keys-{Guid.NewGuid():N}");
    private readonly List<WebApplicationFactory<Program>> _fabriken = [];

    public void Dispose()
    {
        foreach (var fabrik in _fabriken)
        {
            fabrik.Dispose();
        }

        Aufbau.Wegräumen(_path);

        if (Directory.Exists(_keys))
        {
            Directory.Delete(_keys, recursive: true);
        }
    }

    [Fact]
    public async Task Ein_Google_Token_wird_zur_Sitzung_die_ohne_Token_weitertraegt()
    {
        var client = Bauen().CreateClient();

        var sitzung = await Einloesen(client, Token("111", "anna@example.org", name: "Anna", bild: "https://bild"));
        sitzung.EnsureSuccessStatusCode();
        var konto = await sitzung.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Anna", konto.GetProperty("name").GetString());
        Assert.Equal("anna@example.org", konto.GetProperty("email").GetString());
        Assert.Equal("https://bild", konto.GetProperty("picture").GetString());

        var cookie = Assert.Single(sitzung.Headers.GetValues("Set-Cookie"));
        Assert.Contains("matchday.session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // Ab jetzt kein Token mehr — das Cookie trägt, und das Konto ist dasselbe.
        var ich = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("anna@example.org", ich.GetProperty("email").GetString());

        var angelegt = await client.PostAsJsonAsync("/api/tournaments", new { name = "Cup" });
        angelegt.EnsureSuccessStatusCode();
        Assert.Single(await client.GetFromJsonAsync<JsonElement[]>("/api/tournaments") ?? []);

        // Abmelden löscht die Sitzung; danach ist wieder zu.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tournaments")).StatusCode);
    }

    [Fact]
    public async Task Hinter_TLS_bekommt_das_Cookie_Secure()
    {
        // Railway reicht https als Kopfzeile herein — ohne sie wäre das Cookie
        // auch über unverschlüsseltes http zu haben.
        var client = Bauen().CreateClient();
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token("1", "anna@example.org"));
        anfrage.Headers.Add("X-Forwarded-Proto", "https");

        var antwort = await client.SendAsync(anfrage);

        Assert.Contains("secure", Assert.Single(antwort.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ein_fremdes_Konto_bekommt_gar_keine_Sitzung()
    {
        var client = Bauen(freigegeben: "anna@example.org").CreateClient();

        var antwort = await Einloesen(client, Token("222", "tom@example.org"));

        Assert.Equal(HttpStatusCode.Forbidden, antwort.StatusCode);
        Assert.False(antwort.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Ohne_gueltiges_Token_gibt_es_keine_Sitzung()
    {
        var client = Bauen().CreateClient();

        // Ohne Token, mit fremdem Schlüssel, für eine andere Anwendung, abgelaufen.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/session", null)).StatusCode);
        var fremd = new SymmetricSecurityKey(new byte[32]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Einloesen(client, Token("3", "a@example.org", schluessel: fremd))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Einloesen(client, Token("3", "a@example.org", audience: "andere-app"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Einloesen(client, Token("3", "a@example.org", ablauf: DateTime.UtcNow.AddHours(-2)))).StatusCode);
    }

    [Fact]
    public async Task Auch_ohne_abgebildete_Namen_findet_die_Sitzung_Subjekt_und_Adresse()
    {
        // Ohne Abbildung heißen die Angaben „sub" und „email" statt der langen
        // Namen — die Sitzung muss beide Schreibweisen lesen.
        var client = Bauen(abbilden: false).CreateClient();

        var antwort = await Einloesen(client, Token("333", "eva@example.org"));
        antwort.EnsureSuccessStatusCode();
        var konto = await antwort.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("eva@example.org", konto.GetProperty("email").GetString());
        Assert.Equal(string.Empty, konto.GetProperty("name").GetString());
        (await client.PostAsJsonAsync("/api/tournaments", new { name = "Cup" })).EnsureSuccessStatusCode();

        // Ein Konto ganz ohne Adresse kommt ohne Freigabeliste herein — und
        // steht dann eben ohne Adresse in der Kopfzeile.
        var ohne = await Einloesen(client, Token("444", null));
        Assert.Equal(string.Empty, (await ohne.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Die_Schluessel_liegen_dort_wo_sie_hingehoeren()
    {
        var client = Bauen(schluesselPfad: _keys).CreateClient();

        (await Einloesen(client, Token("4", "anna@example.org"))).EnsureSuccessStatusCode();

        Assert.NotEmpty(Directory.GetFiles(_keys, "*.xml"));
    }

    [Fact]
    public async Task Ohne_Schalter_gibt_es_keine_Sitzung_und_nichts_abzumelden()
    {
        var client = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path};Pooling=False"));
        _fabriken.Add(client);
        var http = client.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/auth/session", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Die_Sitzung_oeffnet_die_Mitschau_der_eigenen_Turniere_ohne_Link()
    {
        var fabrik = Bauen(freigegeben: "anna@example.org");
        var anna = fabrik.CreateClient();
        (await Einloesen(anna, Token("555", "anna@example.org"))).EnsureSuccessStatusCode();
        var angelegt = await (await anna.PostAsJsonAsync("/api/tournaments", new { name = "Cup" })).Content.ReadFromJsonAsync<JsonElement>();
        var id = angelegt.GetProperty("tournament").GetProperty("id").GetString();
        var mitschau = angelegt.GetProperty("links").GetProperty("publicUrl").GetString()!.Split("?t=")[1];

        // Die eigene Sitzung genügt — so wie ein EventSource sie mitschickt.
        Assert.Equal("event: view", await ErsteZeile(anna, $"/api/tournaments/{id}/live"));

        // Ohne Sitzung, oder mit einem Konto, das nicht freigegeben ist, ist
        // man hier niemand: ohne Link zu, mit Link offen.
        var anonym = fabrik.CreateClient();
        var tom = fabrik.CreateClient();
        tom.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("666", "tom@example.org"));

        foreach (var wer in new[] { anonym, tom })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await wer.GetAsync($"/api/tournaments/{id}/live")).StatusCode);
            Assert.Equal("event: view", await ErsteZeile(wer, $"/api/tournaments/{id}/live?key={mitschau}"));
        }
    }

    private static async Task<string?> ErsteZeile(HttpClient client, string adresse)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var antwort = await client.GetAsync(adresse, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await antwort.Content.ReadAsStreamAsync(cts.Token));
        return await reader.ReadLineAsync(cts.Token);
    }

    private static Task<HttpResponseMessage> Einloesen(HttpClient client, string token)
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/api/auth/session");
        anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(anfrage);
    }

    private static string Token(
        string subjekt,
        string? email,
        string? name = null,
        string? bild = null,
        SecurityKey? schluessel = null,
        string audience = ClientId,
        DateTime? ablauf = null)
    {
        var angaben = new Dictionary<string, object> { ["sub"] = subjekt };

        if (email is not null)
        {
            angaben["email"] = email;
            angaben["email_verified"] = true;
        }

        if (name is not null)
        {
            angaben["name"] = name;
        }

        if (bild is not null)
        {
            angaben["picture"] = bild;
        }

        var bis = ablauf ?? DateTime.UtcNow.AddHours(1);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = audience,
            Claims = angaben,
            NotBefore = bis.AddHours(-2),
            IssuedAt = bis.AddHours(-2),
            Expires = bis,
            SigningCredentials = new SigningCredentials(schluessel ?? Schluessel, SecurityAlgorithms.HmacSha256),
        });
    }

    private WebApplicationFactory<Program> Bauen(string? freigegeben = null, bool abbilden = true, string? schluesselPfad = null)
    {
        var fabrik = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path};Pooling=False");
            builder.UseSetting("Auth:Required", "true");
            builder.UseSetting("Auth:GoogleClientId", ClientId);

            if (freigegeben is not null)
            {
                builder.UseSetting("Auth:AllowedEmails", freigegeben);
            }

            if (schluesselPfad is not null)
            {
                builder.UseSetting("Auth:KeysPath", schluesselPfad);
            }

            // Statt Googles Schlüsseln aus dem Netz genau der eine des Tests.
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.MapInboundClaims = abbilden;
                    options.Configuration = new OpenIdConnectConfiguration();
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                    options.TokenValidationParameters.IssuerSigningKey = Schluessel;
                }));
        });

        _fabriken.Add(fabrik);
        return fabrik;
    }
}
