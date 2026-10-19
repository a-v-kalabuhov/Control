using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.AppInfo;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.AppInfo;

[Collection("Integration")]
public class AppInfoTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public AppInfoTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAppInfo_WithoutToken_ReturnsDemoModeFromConfiguration()
    {
        var expected = _factory.Services.GetRequiredService<IConfiguration>().GetValue<bool>("DemoMode");
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/app-info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AppInfoDto>();
        dto!.DemoMode.Should().Be(expected);
    }
}
