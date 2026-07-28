using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Rauchtest der Composition Root. Er prüft weniger den Endpunkt selbst als die
/// Verdrahtung: dass <see cref="WebApplicationFactory{TEntryPoint}"/> die API
/// hochfährt und die Registrierungen auflösen kann.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_antwortet_mit_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
