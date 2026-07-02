using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldQrTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldQrTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMoldQr_PayloadIdEqualsMoldGuid_NotFormId()
    {
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold
            {
                FormId = "ART-QR-001",
                Name = "ПФ-QR-тест",
                Cavities = 1,
                IsActive = true
            };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var dto = await client.GetFromJsonAsync<QrCodeDto>(
            $"/api/molds/{moldId}/qr",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var payload = JsonSerializer.Deserialize<JsonElement>(dto!.QrData);
        payload.GetProperty("entity").GetString().Should().Be("mold");
        payload.GetProperty("id").GetString().Should().Be(moldId.ToString());
        payload.GetProperty("id").GetString().Should().NotBe("ART-QR-001");
        payload.TryGetProperty("name", out _).Should().BeFalse("в QR зашивается только id");
    }
}
