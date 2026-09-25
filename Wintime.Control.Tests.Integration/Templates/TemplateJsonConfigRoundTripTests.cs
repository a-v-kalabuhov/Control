using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Template;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Templates;

/// <summary>
/// jsonConfig шаблона должен переживать сохранение без семантических потерь —
/// на этом держится круговой импорт/экспорт шаблонов (фронт сравнивает данные, не байты).
/// Create и Update сохраняют jsonConfig разными путями, поэтому проверяем оба.
/// </summary>
[Collection("Integration")]
public class TemplateJsonConfigRoundTripTests : IClassFixture<IntegrationTestFactory>
{
    private const string ConfigJson =
        """{"device_timeout_seconds": 30, "sensors": [{"name": "Температура зоны 1", "field": "temp_zone_1", "type": "float", "threshold": 0.5}, {"name": "Давление, бар", "field": "pressure", "type": "float", "threshold": 1.5}]}""";

    private readonly IntegrationTestFactory _factory;
    public TemplateJsonConfigRoundTripTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "admin", "Admin123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static object NewTemplate(string name, string configJson) => new
    {
        name,
        manufacturer = "Haitian",
        model = "MA1200",
        version = "1.0",
        author = "Wintime",
        connectorType = "usr-modbus",
        jsonConfig = JsonNode.Parse(configJson)
    };

    private static void AssertSameJson(string actual, string expected) =>
        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected))
            .Should().BeTrue($"сохранённый jsonConfig {actual} должен совпадать с {expected}");

    private static async Task<Guid> CreateAsync(HttpClient client, string name, string configJson)
    {
        var resp = await client.PostAsJsonAsync("/api/templates", NewTemplate(name, configJson));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<TemplateDto>();
        return dto!.Id;
    }

    [Fact]
    public async Task Create_PreservesJsonConfigSemantics()
    {
        var client = await AdminClientAsync();
        var id = await CreateAsync(client, $"RT-create-{Guid.NewGuid():N}", ConfigJson);

        var saved = await client.GetFromJsonAsync<TemplateDto>($"/api/templates/{id}");

        AssertSameJson(saved!.JsonConfig, ConfigJson);
    }

    [Fact]
    public async Task Update_PreservesJsonConfigSemantics()
    {
        var client = await AdminClientAsync();
        var name = $"RT-update-{Guid.NewGuid():N}";
        var id = await CreateAsync(client, name, """{"sensors": []}""");

        var put = await client.PutAsJsonAsync($"/api/templates/{id}", NewTemplate(name, ConfigJson));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var saved = await client.GetFromJsonAsync<TemplateDto>($"/api/templates/{id}");

        AssertSameJson(saved!.JsonConfig, ConfigJson);
    }
}
