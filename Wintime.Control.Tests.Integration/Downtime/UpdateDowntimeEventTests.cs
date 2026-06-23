using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Downtime;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Downtime;

[Collection("Integration")]
public class UpdateDowntimeEventTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public UpdateDowntimeEventTests(IntegrationTestFactory factory) => _factory = factory;

    /// <summary>
    /// Наладчик редактирует простой: меняет причину, время окончания и комментарий.
    /// PATCH должен быть доступен роли Adjuster (не только Admin/Manager), и
    /// должен применять все три поля, денормализуя ReasonName из найденной причины.
    /// </summary>
    [Fact]
    public async Task Adjuster_CanUpdate_Reason_EndTime_Comment()
    {
        // Arrange: ТПА + причина + открытый авто-простой в БД.
        Guid eventId;
        Guid reasonId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);

            var reason = new DowntimeReason { Name = "Нет материала", Type = "downtime", IsActive = true };
            db.DowntimeReasons.Add(reason);

            var evt = new Event
            {
                ImmId = imm.Id,
                EventType = EventType.Downtime,
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = null,
                IsAuto = true
            };
            db.Events.Add(evt);

            await db.SaveChangesAsync();
            eventId = evt.Id;
            reasonId = reason.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var body = new UpdateDowntimeEventRequestDto
        {
            ReasonId = reasonId,
            EndTime = DateTime.UtcNow,
            Comment = "Ждали поставку сырья"
        };

        // Act
        var resp = await client.PatchAsJsonAsync($"/api/downtime/events/{eventId}", body);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var evt = await db.Events.FindAsync(eventId);
            evt.Should().NotBeNull();
            evt!.ReasonId.Should().Be(reasonId);
            evt.ReasonName.Should().Be("Нет материала");
            evt.EndTime.Should().NotBeNull();
            evt.Comment.Should().Be("Ждали поставку сырья");
        }
    }
}
