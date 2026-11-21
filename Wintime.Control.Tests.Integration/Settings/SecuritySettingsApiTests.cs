using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Settings;

/// <summary>Настройки безопасности: политика паролей, по умолчанию строгая.</summary>
[Collection("Integration")]
public class SecuritySettingsApiTests : IClassFixture<IntegrationTestFactory>
{
    private const string Url = "/api/settings/security";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IntegrationTestFactory _factory;
    public SecuritySettingsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task SecuritySettings_DefaultFalse_AndUpdatePersists()
    {
        var client = await ClientAsync("test_manager", "Manager123!");

        var initial = await client.GetFromJsonAsync<JsonElement>(Url, JsonOptions);
        initial.GetProperty("allowWeakPasswords").GetBoolean().Should().BeFalse();
        initial.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);

        try
        {
            var response = await client.PutAsJsonAsync(Url, new { allowWeakPasswords = true });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await client.GetFromJsonAsync<JsonElement>(Url, JsonOptions);
            saved.GetProperty("allowWeakPasswords").GetBoolean().Should().BeTrue();
            saved.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.String);
        }
        finally
        {
            await client.PutAsJsonAsync(Url, new { allowWeakPasswords = false });
        }
    }

    [Fact]
    public async Task SecuritySettings_Adjuster_Returns403()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Url, new { allowWeakPasswords = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
