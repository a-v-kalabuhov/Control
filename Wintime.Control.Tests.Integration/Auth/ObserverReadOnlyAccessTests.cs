using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Auth;

/// <summary>PZP-07a: Observer видит раздел «Планирование» целиком, но только на чтение.</summary>
[Collection("Integration")]
public class ObserverReadOnlyAccessTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ObserverReadOnlyAccessTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ObserverClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_observer", "Observer123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/molds")]
    [InlineData("/api/producttypes")]
    [InlineData("/api/personnel?isActive=true&role=Adjuster")]
    public async Task Observer_can_read_lists(string url)
    {
        var client = await ObserverClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/orders/{0}")]
    [InlineData("/api/tasks/{0}")]
    [InlineData("/api/molds/{0}")]
    [InlineData("/api/producttypes/{0}")]
    public async Task Observer_reads_by_id_gets_404_not_403(string urlFormat)
    {
        var client = await ObserverClientAsync();

        var response = await client.GetAsync(string.Format(urlFormat, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Observer_cannot_create_order()
    {
        var client = await ObserverClientAsync();

        var response = await client.PostAsJsonAsync("/api/orders", new { number = "ORD-OBS", quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/orders/{0}/complete")]
    [InlineData("/api/orders/{0}/cancel")]
    [InlineData("/api/orders/{0}/reopen")]
    public async Task Observer_cannot_change_order_status(string urlFormat)
    {
        var client = await ObserverClientAsync();

        var response = await client.PostAsync(string.Format(urlFormat, Guid.NewGuid()), null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Observer_cannot_detach_task_from_order()
    {
        var client = await ObserverClientAsync();

        var response = await client.DeleteAsync($"/api/orders/{Guid.NewGuid()}/tasks/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
