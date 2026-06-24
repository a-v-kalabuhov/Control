using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetImmList_NoTaskIdleCache_ReturnsNoTask()
    {
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);
            await db.SaveChangesAsync();
            immId = imm.Id;

            var cache = scope.ServiceProvider.GetRequiredService<IImmStatusCache>();
            cache.SetStatus(immId, ImmStatus.Idle, DateTime.UtcNow);
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var imms = await client.GetFromJsonAsync<List<ImmDto>>("/api/imm?isActive=true");

        imms.Should().NotBeNull();
        imms!.Single(i => i.Id == immId).EffectiveStatus.Should().Be(EffectiveStatus.NoTask);
    }
}
