using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Ports;
using TennisTurnier.Application.Security;
using TennisTurnier.Application.Tournaments;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der erste Systemadministrator kommt aus der Konfiguration, weil er sonst
/// nirgends herkommen könnte: Rollen vergibt, wer eine hat, und nach einer
/// frischen Migration hat niemand eine.
///
/// Geprüft wird durchgehend an der Wirkung, nicht an der Zeile in der Tabelle:
/// eine Zuweisung, die der Query-Filter aus ADR-0004 nicht erreicht, wäre keine.
/// Die Wirkung war einmal „darf einen Verein anlegen". Das taugt nicht mehr —
/// Turniere anlegen darf seit dem Selbstservice jeder. Sie ist jetzt „sieht ein
/// fremdes Turnier": genau das kann ausschließlich der Systemadministrator.
/// </summary>
public sealed class BootstrapAdminTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string AdminEmail = "erster.admin@example.invalid";

    /// <summary>
    /// Legt als ein anderer Benutzer ein Turnier an und liefert, wie viele
    /// Turniere der geprüfte Client davon sieht.
    ///
    /// Null heißt: er sieht nur, was ihm gehört. Eins heißt: er sieht alles.
    /// </summary>
    private static async Task<int> FremdeTurniereAsync(
        TennisTurnierApiFactory factory,
        HttpClient client)
    {
        await factory.NeuesTurnierAsync(
            $"fremder-{Guid.NewGuid():N}",
            new TurnierWunsch { Anlage = "TC Fremd", Auslosen = false });

        var meine = await client.GetFromJsonAsync<List<TournamentSummary>>("/api/tournaments", Json);

        return meine!.Count;
    }

    [Fact]
    public async Task Wer_in_der_Konfiguration_steht_wird_bei_der_Anmeldung_Systemadministrator()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("bootstrap-per-mail", AdminEmail);

        Assert.Equal(1, await FremdeTurniereAsync(factory, client));
    }

    [Fact]
    public async Task Auch_die_Subject_ID_wird_erkannt()
    {
        // Nicht jeder Aussteller legt eine E-Mail in das Token. Ohne diesen Weg
        // bliebe ein solcher Verbund ohne ersten Administrator.
        using var factory = new TennisTurnierApiFactory(["bootstrap-per-subject"]);
        var client = factory.CreateClientAs("bootstrap-per-subject");

        Assert.Equal(1, await FremdeTurniereAsync(factory, client));
    }

    [Fact]
    public async Task Die_Schreibweise_der_E_Mail_spielt_keine_Rolle()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail.ToUpperInvariant()]);
        var client = factory.CreateClientAs("bootstrap-gross-klein", AdminEmail);

        Assert.Equal(1, await FremdeTurniereAsync(factory, client));
    }

    [Fact]
    public async Task Wer_nicht_in_der_Konfiguration_steht_bekommt_nichts()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("fremder", "jemand.anderes@example.invalid");

        Assert.Equal(0, await FremdeTurniereAsync(factory, client));
    }

    [Fact]
    public async Task Ohne_Eintrag_bleibt_jeder_ohne_Rolle()
    {
        using var factory = new TennisTurnierApiFactory();
        var client = factory.CreateClientAs("niemand", AdminEmail);

        Assert.Equal(0, await FremdeTurniereAsync(factory, client));
    }

    [Fact]
    public async Task Ein_Systemadministrator_bekommt_die_Veranstalterrolle_nicht_zusaetzlich()
    {
        // Er darf ohnehin alles; eine zweite Zuweisung wäre eine Zeile ohne
        // Wirkung, die bei jedem Request neu geschrieben werden müsste.
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("bootstrap-ohne-organizer", AdminEmail);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.True(me!.IsSystemAdmin);
        Assert.DoesNotContain(me.Roles, r => r.Role == Role.Organizer);
    }

    [Fact]
    public async Task Wer_sich_anmeldet_wird_Veranstalter()
    {
        // Der Selbstservice: kein Eintrag in einer Konfigurationsdatei, keine
        // Freischaltung durch jemand anderen. Die Rolle wird ausdrücklich
        // vergeben und steht deshalb in der Auskunft — eine unsichtbare Regel im
        // Code wäre weder abfragbar noch entziehbar.
        using var factory = new TennisTurnierApiFactory();
        var client = factory.CreateClientAs("frisch-angemeldet");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/me", Json);

        Assert.False(me!.IsSystemAdmin);
        Assert.Contains(me.Roles, r => r.Role == Role.Organizer && r.Scope == ScopeType.Global);
    }

    [Fact]
    public async Task Mehrfache_Anmeldung_vergibt_die_Rolle_nur_einmal()
    {
        // Die Vergabe hängt an jedem Request, nicht an einem einmaligen Start.
        // Ohne Idempotenz sammelte ein Administrator mit jedem Aufruf eine
        // weitere Zuweisung an.
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("bootstrap-mehrfach", AdminEmail);

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(await client.GetFromJsonAsync<MeResponse>("/api/me", Json));
        }

        using var scope = factory.CreateMigratedScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var account = await directory.EnsureAccountAsync(
            TennisTurnierApiFactory.TestIssuer,
            "bootstrap-mehrfach",
            AdminEmail,
            "bootstrap-mehrfach");

        var assignments = await directory.GetAssignmentsAsync(account.Id);

        Assert.Single(assignments, a => a.Role == Role.SystemAdmin);
    }

    [Fact]
    public async Task Auch_die_Veranstalterrolle_entsteht_nur_einmal()
    {
        using var factory = new TennisTurnierApiFactory();
        var client = factory.CreateClientAs("organizer-mehrfach");

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(await client.GetFromJsonAsync<MeResponse>("/api/me", Json));
        }

        using var scope = factory.CreateMigratedScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var account = await directory.EnsureAccountAsync(
            TennisTurnierApiFactory.TestIssuer,
            "organizer-mehrfach",
            email: null,
            displayName: "organizer-mehrfach");

        Assert.Single(await directory.GetAssignmentsAsync(account.Id), a => a.Role == Role.Organizer);
    }
}
