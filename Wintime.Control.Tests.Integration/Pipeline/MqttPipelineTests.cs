using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Behaviors;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Integration.Pipeline;

/// <summary>
/// Интеграционные тесты всего pipeline обработки MQTT-сообщений.
/// Каждый тест создаёт отдельный ТПА (свой ImmId), чтобы записи
/// в Telemetry и ImmStatusHistory не пересекались между тестами.
/// Тест запускает MessageProcessingPipeline напрямую — без MQTT-брокера.
/// </summary>
[Collection("Integration")]
public class MqttPipelineTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public MqttPipelineTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // Телеметрия
    // =========================================================================

    /// <summary>
    /// Корректное MQTT-сообщение должно создать одну строку в таблице Telemetry
    /// с правильными ImmId, ParameterName и ValueNumeric.
    /// Это «happy path» — проверяет весь путь от сырого JSON до записи в БД.
    /// </summary>
    [Fact]
    public async Task ValidMessage_WritesTelemetryRowToDatabase()
    {
        var immId = await _factory.CreateFreshImmAsync();

        await RunPipelineAsync(BuildContext(immId, temp: "25.5"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var row = await db.Telemetry
            .AsNoTracking()
            .SingleAsync(r => r.ImmId == immId);

        row.ParameterName.Should().Be("temp");
        row.ValueNumeric.Should().Be(25.5m);
        row.ValueText.Should().BeNull();
    }

    /// <summary>
    /// После обработки второго сообщения от того же ТПА в Telemetry должно быть
    /// ровно две строки — pipeline не пропускает и не дедуплицирует сообщения.
    /// </summary>
    [Fact]
    public async Task TwoMessages_WritesTwoTelemetryRows()
    {
        var immId = await _factory.CreateFreshImmAsync();

        await RunPipelineAsync(BuildContext(immId, temp: "20.0"));
        await RunPipelineAsync(BuildContext(immId, temp: "21.0"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var count = await db.Telemetry
            .AsNoTracking()
            .CountAsync(r => r.ImmId == immId);

        count.Should().Be(2);
    }

    // =========================================================================
    // Статус ТПА
    // =========================================================================

    /// <summary>
    /// Первое сообщение с mode="auto" должно создать открытую запись
    /// в ImmStatusHistory со статусом "Auto" и EndedAt = null.
    /// </summary>
    [Fact]
    public async Task ValidMessage_CreatesImmStatusHistoryRecord()
    {
        var immId = await _factory.CreateFreshImmAsync();

        await RunPipelineAsync(BuildContext(immId, mode: "auto"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var record = await db.ImmStatusHistory
            .AsNoTracking()
            .SingleAsync(h => h.ImmId == immId);

        record.Status.Should().Be("Auto");
        record.EndedAt.Should().BeNull("запись открыта — статус ещё активен");
    }

    /// <summary>
    /// При смене режима с "auto" на "manual" предыдущая запись ImmStatusHistory
    /// должна быть закрыта (EndedAt != null), а новая — открыта со статусом "Manual".
    /// </summary>
    [Fact]
    public async Task StatusTransition_AutoToManual_ClosesOldAndOpensNew()
    {
        var immId = await _factory.CreateFreshImmAsync();

        await RunPipelineAsync(BuildContext(immId, mode: "auto"));
        await RunPipelineAsync(BuildContext(immId, mode: "manual"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var records = await db.ImmStatusHistory
            .AsNoTracking()
            .Where(h => h.ImmId == immId)
            .ToListAsync();

        records.Should().HaveCount(2);
        records.Should().ContainSingle(r => r.Status == "Auto"   && r.EndedAt != null,
            "запись Auto должна быть закрыта");
        records.Should().ContainSingle(r => r.Status == "Manual" && r.EndedAt == null,
            "запись Manual должна быть открыта");
    }

    // =========================================================================
    // Некорректные сообщения
    // =========================================================================

    /// <summary>
    /// Сообщение с неверным форматом топика (не-GUID в сегменте deviceId) должно быть
    /// отброшено на этапе декодирования — количество записей в Telemetry и ImmStatusHistory
    /// не должно увеличиться.
    /// </summary>
    [Fact]
    public async Task InvalidTopicFormat_WritesNothingToDatabase()
    {
        // Снимаем счётчики до запуска — другие тесты уже могли добавить строки
        using var before = _factory.Services.CreateScope();
        var beforeDb = before.ServiceProvider.GetRequiredService<ControlDbContext>();
        var telemetryBefore   = await beforeDb.Telemetry.AsNoTracking().CountAsync();
        var statusHistBefore  = await beforeDb.ImmStatusHistory.AsNoTracking().CountAsync();

        var context = new MqttProcessingContext(
            Guid.NewGuid(),
            Topic:   "control/imm/not-a-guid/telemetry",
            Payload: """{"timestamp": 1700000000, "mode": "auto", "sensors": {"temp": "25.5"}}""",
            Data: null, Device: null, Template: null);

        await RunPipelineAsync(context);

        using var after = _factory.Services.CreateScope();
        var afterDb = after.ServiceProvider.GetRequiredService<ControlDbContext>();

        (await afterDb.Telemetry.AsNoTracking().CountAsync())
            .Should().Be(telemetryBefore, "невалидный топик не должен добавлять строки в Telemetry");
        (await afterDb.ImmStatusHistory.AsNoTracking().CountAsync())
            .Should().Be(statusHistBefore, "невалидный топик не должен добавлять записи статусов");
    }

    /// <summary>
    /// Сообщение с валидным топиком, но несуществующим deviceId должно быть
    /// отброшено — ни одной записи в БД создано не будет.
    /// </summary>
    [Fact]
    public async Task UnknownDevice_WritesNothingToDatabase()
    {
        var unknownId = Guid.NewGuid(); // не добавляли в БД

        await RunPipelineAsync(BuildContext(unknownId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        (await db.Telemetry.AsNoTracking()
            .AnyAsync(r => r.ImmId == unknownId)).Should().BeFalse();
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private async Task RunPipelineAsync(MqttProcessingContext context)
    {
        using var scope = _factory.Services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<MessageProcessingPipeline>();
        await pipeline.ProcessAsync(context, CancellationToken.None);
    }

    private static MqttProcessingContext BuildContext(
        Guid immId,
        string temp = "25.5",
        string mode = "auto")
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload   = $$$"""{"timestamp": {{{timestamp}}}, "mode": "{{{mode}}}", "sensors": {"temp": {{{temp}}}}}""";

        return new MqttProcessingContext(
            Guid.NewGuid(),
            Topic:   $"control/imm/{immId}/telemetry",
            Payload: payload,
            Data: null, Device: null, Template: null);
    }
}
