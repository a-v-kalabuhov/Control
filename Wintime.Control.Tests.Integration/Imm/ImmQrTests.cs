using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class ImmQrTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ImmQrTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetImmQr_ExistingImm_ReturnsMachinePayloadWithGuidId()
    {
        Guid immId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = "ТПА-QR-тест",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var response = await client.GetAsync($"/api/imm/{immId}/qr");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<QrCodeDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dto!.EntityType.Should().Be("machine");
        dto.EntityId.Should().Be(immId.ToString());

        var payload = JsonSerializer.Deserialize<JsonElement>(dto.QrData);
        payload.GetProperty("entity").GetString().Should().Be("machine");
        payload.GetProperty("id").GetString().Should().Be(immId.ToString());
        payload.TryGetProperty("name", out _).Should().BeFalse("в QR зашивается только id");
    }

    [Fact]
    public async Task GetImmQr_UnknownId_Returns404()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var response = await client.GetAsync($"/api/imm/{Guid.NewGuid()}/qr");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
