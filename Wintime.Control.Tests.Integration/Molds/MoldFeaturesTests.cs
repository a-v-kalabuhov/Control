using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldFeaturesTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public MoldFeaturesTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<HttpResponseMessage> CreateMoldAsync(
        HttpClient client, IReadOnlyDictionary<string, object?>? features = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["formId"] = $"PF-{Guid.NewGuid():N}",
            ["name"] = "ПФ с особенностями",
            ["cavities"] = 1,
            ["maxResourceCycles"] = 1000,
            ["productTypeId"] = _factory.TestProductTypeId
        };
        if (features != null)
        {
            foreach (var (key, value) in features)
                payload[key] = value;
        }
        return await client.PostAsJsonAsync("/api/molds", payload);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void FeaturesShouldBe(JsonElement dto, bool movingParts, bool robot, bool conveyor)
    {
        dto.GetProperty("hasMovingParts").GetBoolean().Should().Be(movingParts);
        dto.GetProperty("requiresRobot").GetBoolean().Should().Be(robot);
        dto.GetProperty("requiresConveyor").GetBoolean().Should().Be(conveyor);
    }

    [Fact]
    public async Task CreateMold_WithFeatures_PersistsAndReturnsThem()
    {
        var client = await ManagerClientAsync();

        var response = await CreateMoldAsync(client, new Dictionary<string, object?>
        {
            ["hasMovingParts"] = true,
            ["requiresRobot"] = false,
            ["requiresConveyor"] = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadJsonAsync(response);
        FeaturesShouldBe(created, movingParts: true, robot: false, conveyor: true);

        var loaded = await client.GetAsync($"/api/molds/{created.GetProperty("id").GetGuid()}");
        FeaturesShouldBe(await ReadJsonAsync(loaded), movingParts: true, robot: false, conveyor: true);
    }

    [Fact]
    public async Task CreateMold_WithoutFeatures_DefaultsToFalse()
    {
        var client = await ManagerClientAsync();

        var response = await CreateMoldAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        FeaturesShouldBe(await ReadJsonAsync(response), movingParts: false, robot: false, conveyor: false);
    }

    [Fact]
    public async Task UpdateActiveMold_ChangesFeatures()
    {
        var client = await ManagerClientAsync();
        var createdResponse = await CreateMoldAsync(client);
        var created = await ReadJsonAsync(createdResponse);
        var id = created.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/molds/{id}", new Dictionary<string, object?>
        {
            ["hasMovingParts"] = true,
            ["requiresRobot"] = true,
            ["requiresConveyor"] = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var loaded = await client.GetAsync($"/api/molds/{id}");
        FeaturesShouldBe(await ReadJsonAsync(loaded), movingParts: true, robot: true, conveyor: true);
    }

    [Theory]
    [InlineData("hasMovingParts")]
    [InlineData("requiresRobot")]
    [InlineData("requiresConveyor")]
    public async Task UpdateArchivedMold_ChangingFeature_Returns400(string feature)
    {
        var client = await ManagerClientAsync();
        var createdResponse = await CreateMoldAsync(client);
        var created = await ReadJsonAsync(createdResponse);
        var id = created.GetProperty("id").GetGuid();
        var archiveResponse = await client.PutAsJsonAsync($"/api/molds/{id}", new { isActive = false });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/molds/{id}", new Dictionary<string, object?>
        {
            [feature] = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            "Нельзя изменять особенности архивной пресс-формы.");
        var loaded = await client.GetAsync($"/api/molds/{id}");
        FeaturesShouldBe(await ReadJsonAsync(loaded), movingParts: false, robot: false, conveyor: false);
    }

    [Fact]
    public async Task UpdateArchivedMold_UnchangedFeatures_AllowsOtherChanges()
    {
        var client = await ManagerClientAsync();
        var createdResponse = await CreateMoldAsync(client, new Dictionary<string, object?>
        {
            ["hasMovingParts"] = true,
            ["requiresRobot"] = false,
            ["requiresConveyor"] = true
        });
        var created = await ReadJsonAsync(createdResponse);
        var id = created.GetProperty("id").GetGuid();
        var archiveResponse = await client.PutAsJsonAsync($"/api/molds/{id}", new { isActive = false });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/molds/{id}", new Dictionary<string, object?>
        {
            ["name"] = "Новое наименование",
            ["hasMovingParts"] = true,
            ["requiresRobot"] = false,
            ["requiresConveyor"] = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var loaded = await client.GetAsync($"/api/molds/{id}");
        var dto = await ReadJsonAsync(loaded);
        dto.GetProperty("name").GetString().Should().Be("Новое наименование");
        dto.GetProperty("isActive").GetBoolean().Should().BeFalse();
        FeaturesShouldBe(dto, movingParts: true, robot: false, conveyor: true);
    }

    [Fact]
    public async Task UpdateArchivedMold_RestoreAndChangeFeature_Returns400AndKeepsArchived()
    {
        var client = await ManagerClientAsync();
        var createdResponse = await CreateMoldAsync(client);
        var created = await ReadJsonAsync(createdResponse);
        var id = created.GetProperty("id").GetGuid();
        var archiveResponse = await client.PutAsJsonAsync($"/api/molds/{id}", new { isActive = false });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/molds/{id}", new Dictionary<string, object?>
        {
            ["isActive"] = true,
            ["hasMovingParts"] = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var loaded = await client.GetAsync($"/api/molds/{id}");
        var dto = await ReadJsonAsync(loaded);
        dto.GetProperty("isActive").GetBoolean().Should().BeFalse();
        FeaturesShouldBe(dto, movingParts: false, robot: false, conveyor: false);
    }
}
