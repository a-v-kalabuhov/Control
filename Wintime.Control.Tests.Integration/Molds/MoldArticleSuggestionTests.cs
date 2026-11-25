using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldArticleSuggestionTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldArticleSuggestionTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ArticleSuggestion_UsesLatestMoldAndSkipsOccupiedIncludingArchived()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var now = DateTime.UtcNow;
            db.Molds.AddRange(
                new Mold { FormId = "PF-SUGGEST-0100", Name = "ПФ 0100", Cavities = 1, CreatedAt = now.AddSeconds(1) },
                new Mold { FormId = "PF-SUGGEST-0101", Name = "ПФ 0101", Cavities = 1, CreatedAt = now.AddSeconds(2) },
                new Mold { FormId = "PF-SUGGEST-0102", Name = "ПФ 0102", Cavities = 1, CreatedAt = now.AddSeconds(3), IsActive = false },
                new Mold { FormId = "PF-SUGGEST-0099", Name = "Последняя ПФ", Cavities = 1, CreatedAt = now.AddSeconds(4) });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/molds/article-suggestion");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json", content);
        using var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("article").GetString().Should().Be("PF-SUGGEST-0103");
    }
}
