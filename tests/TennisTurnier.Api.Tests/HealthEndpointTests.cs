using System.Net;

namespace TennisTurnier.Api.Tests;

/// <summary>
/// Rauchtest der Composition Root. Er prüft weniger den Endpunkt selbst als die
/// Verdrahtung: dass die API hochfährt und alle Registrierungen auflösbar sind.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<TennisTurnierApiFactory>
{
    private readonly TennisTurnierApiFactory _factory;

    public HealthEndpointTests(TennisTurnierApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_antwortet_mit_200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
