using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class TaskCompletionDefectTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskCompletionDefectTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<Guid> InProgressTaskAsync(int actual)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var t = new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
            PlanQuantity = 100, ActualQuantity = actual, Status = TaskStatus.InProgress, IssuedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
        db.ShiftTasks.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Complete_WithDefect_StoresDefect()
    {
        var taskId = await InProgressTaskAsync(actual: 100);
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/tasks/{taskId}/complete",
            new CompleteTaskRequestDto { ActualQuantity = 100, DefectQuantity = 15 });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftTasks.FindAsync(taskId))!.DefectQuantity.Should().Be(15);
    }

    [Fact]
    public async Task Complete_WithDefectAboveActual_Returns400()
    {
        var taskId = await InProgressTaskAsync(actual: 100);
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/tasks/{taskId}/complete",
            new CompleteTaskRequestDto { ActualQuantity = 100, DefectQuantity = 101 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
