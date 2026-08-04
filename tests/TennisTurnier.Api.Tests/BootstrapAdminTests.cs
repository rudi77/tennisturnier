using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TennisTurnier.Application.Clubs;
using TennisTurnier.Application.Ports;
using TennisTurnier.Domain.Security;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Der erste Systemadministrator kommt aus der Konfiguration, weil er sonst
/// nirgends herkommen könnte: Rollen vergibt, wer eine hat, und nach einer
/// frischen Migration hat niemand eine.
///
/// Geprüft wird durchgehend an der Wirkung — „darf einen Verein anlegen" —, nicht
/// an der Zeile in der Tabelle. Eine Zuweisung, die der Query-Filter aus ADR-0004
/// nicht erreicht, wäre keine.
/// </summary>
public sealed class BootstrapAdminTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string AdminEmail = "erster.admin@example.invalid";

    private static Task<HttpResponseMessage> CreateClubAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/clubs",
            new CreateClubRequest($"TC Bootstrap {Guid.NewGuid():N}", "Europe/Vienna", null),
            Json);

    [Fact]
    public async Task Wer_in_der_Konfiguration_steht_wird_bei_der_Anmeldung_Systemadministrator()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("bootstrap-per-mail", AdminEmail);

        var response = await CreateClubAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Auch_die_Subject_ID_wird_erkannt()
    {
        // Nicht jeder Aussteller legt eine E-Mail in das Token. Ohne diesen Weg
        // bliebe ein solcher Verbund ohne ersten Administrator.
        using var factory = new TennisTurnierApiFactory(["bootstrap-per-subject"]);
        var client = factory.CreateClientAs("bootstrap-per-subject");

        var response = await CreateClubAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Die_Schreibweise_der_E_Mail_spielt_keine_Rolle()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail.ToUpperInvariant()]);
        var client = factory.CreateClientAs("bootstrap-gross-klein", AdminEmail);

        var response = await CreateClubAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Wer_nicht_in_der_Konfiguration_steht_bekommt_nichts()
    {
        using var factory = new TennisTurnierApiFactory([AdminEmail]);
        var client = factory.CreateClientAs("fremder", "jemand.anderes@example.invalid");

        var response = await CreateClubAsync(client);

        // 404 und nicht 403: ein 403 bestätigte die Existenz der Ressource (ADR-0004).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ohne_Eintrag_bleibt_jeder_ohne_Rolle()
    {
        using var factory = new TennisTurnierApiFactory();
        var client = factory.CreateClientAs("niemand", AdminEmail);

        var response = await CreateClubAsync(client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            Assert.Equal(HttpStatusCode.Created, (await CreateClubAsync(client)).StatusCode);
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
}
