using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Settings;

/// <summary>PZP-14: GET/PUT /api/settings/planning-defaults — сохранение, пределы, права.</summary>
[Collection("Integration")]
public class PlanningDefaultsApiTests : IClassFixture<IntegrationTestFactory>
{
    private const string Url = "/api/settings/planning-defaults";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IntegrationTestFactory _factory;
    public PlanningDefaultsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task UpdatePlanningDefaults_Valid_Persists()
    {
        var client = await ClientAsync("test_manager", "Manager123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = 90, releaseBufferFixedMinutes = 30, releaseBufferPercent = 10 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await client.GetFromJsonAsync<JsonElement>(Url, JsonOptions);
        saved.GetProperty("setupMinutes").GetInt32().Should().Be(90);
        saved.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(30);
        saved.GetProperty("releaseBufferPercent").GetInt32().Should().Be(10);
        saved.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task UpdatePlanningDefaults_Admin_Allowed()
    {
        var client = await ClientAsync("admin", "Admin123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = 60, releaseBufferFixedMinutes = 0, releaseBufferPercent = 0 });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(1441, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1441, 0)]
    [InlineData(0, 0, 101)]
    [InlineData(0, 0, -1)]
    public async Task UpdatePlanningDefaults_OutOfRange_Returns400(int setup, int fixedMinutes, int percent)
    {
        var client = await ClientAsync("test_manager", "Manager123!");

        var resp = await client.PutAsJsonAsync(Url,
            new { setupMinutes = setup, releaseBufferFixedMinutes = fixedMinutes, releaseBufferPercent = percent });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlanningDefaults_Adjuster_Returns403()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Url,
            new { setupMinutes = 1, releaseBufferFixedMinutes = 1, releaseBufferPercent = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
