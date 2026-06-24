using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusHistoryTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusHistoryTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_Production_Segment_For_Auto_InProgress()
    {
        var from = new DateTime(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true
            };
            db.Imms.Add(imm);

            var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();

            db.ImmStatusHistory.Add(new ImmStatusHistory
            { ImmId = imm.Id, Status = ImmStatus.Auto, ChangedAt = from, EndedAt = to });

            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100,
                Status = TaskStatus.InProgress, SetupStartedAt = from, StartedAt = from, CompletedAt = to
            });
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var url = $"/api/imm/{immId}/effective-status-history?from={from:O}&to={to:O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var segs = await resp.Content.ReadFromJsonAsync<List<EffectiveStatusSegmentDto>>();
        segs.Should().NotBeNull();
        segs!.Should().ContainSingle();
        segs[0].EffectiveStatus.Should().Be(EffectiveStatus.Production);
    }
}
