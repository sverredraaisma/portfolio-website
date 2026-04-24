using System.Net;
using System.Text.Json;
using FluentAssertions;
using PortfolioApi.Tests.Infrastructure;

namespace PortfolioApi.Tests.E2e;

public class HealthEndpointsE2eTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public HealthEndpointsE2eTests(AppFactory app) => _app = app;

    [Fact]
    public async Task Liveness_returns_200_with_status_ok()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Legacy_health_alias_is_kept()
    {
        var client = _app.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_returns_200_when_the_DB_is_reachable()
    {
        await _app.ResetDatabaseAsync();
        var client = _app.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("ready");
    }
}
