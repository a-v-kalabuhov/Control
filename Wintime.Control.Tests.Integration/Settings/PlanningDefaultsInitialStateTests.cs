using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Settings;

/// <summary>PZP-14: миграция создаёт строку умолчаний с нулями.</summary>
[Collection("Integration")]
public class PlanningDefaultsInitialStateTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PlanningDefaultsInitialStateTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetPlanningDefaults_Initially_AllZero()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var d = await client.GetFromJsonAsync<JsonElement>("/api/settings/planning-defaults",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        d.GetProperty("setupMinutes").GetInt32().Should().Be(0);
        d.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(0);
        d.GetProperty("releaseBufferPercent").GetInt32().Should().Be(0);
        d.GetProperty("conflictThresholdMinutes").GetInt32().Should().Be(20);
        d.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
