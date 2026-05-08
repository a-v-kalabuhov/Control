using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class StoreTelemetryDataHandlerTests : IDisposable
{
    private readonly ControlDbContext _dbContext;

    public StoreTelemetryDataHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ControlDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private StoreTelemetryDataHandler CreateSut()
        => new(_dbContext, NullLogger<StoreTelemetryDataHandler>.Instance);

    // =========================================================================
    // Маппинг типов датчиков в колонки
    // =========================================================================

    /// <summary>
    /// Датчик типа <c>float</c> с корректным числовым значением должен сохраняться
    /// в колонку <c>ValueNumeric</c>, а <c>ValueText</c> остаётся <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_FloatSensor_StoresValueNumeric()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["temp"] = "25.5" },
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueNumeric.Should().Be(25.5m);
        row.ValueText.Should().BeNull();
    }

    /// <summary>
    /// Датчик типа <c>int</c> с корректным целочисленным значением должен сохраняться
    /// в колонку <c>ValueNumeric</c>, а <c>ValueText</c> остаётся <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_IntSensor_StoresValueNumeric()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["count"] = "42" },
            templateSensors: [PipelineTestFixtures.MakeSensor("count", "int")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueNumeric.Should().Be(42m);
        row.ValueText.Should().BeNull();
    }

    /// <summary>
    /// Датчик типа <c>cycleCounter</c> должен сохраняться в <c>ValueNumeric</c>,
    /// так как обрабатывается тем же путём, что и <c>int</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_CycleCounterSensor_StoresValueNumeric()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["cycles"] = "1000" },
            templateSensors: [PipelineTestFixtures.MakeSensor("cycles", "cycleCounter")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueNumeric.Should().Be(1000m);
        row.ValueText.Should().BeNull();
    }

    /// <summary>
    /// Датчик типа <c>string</c> должен сохраняться в колонку <c>ValueText</c>,
    /// а <c>ValueNumeric</c> остаётся <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_StringSensor_StoresValueText()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["label"] = "running" },
            templateSensors: [PipelineTestFixtures.MakeSensor("label", "string")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueText.Should().Be("running");
        row.ValueNumeric.Should().BeNull();
    }

    /// <summary>
    /// Датчик типа <c>boolean</c> должен сохраняться в колонку <c>ValueText</c>,
    /// а <c>ValueNumeric</c> остаётся <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_BooleanSensor_StoresValueText()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["door"] = "true" },
            templateSensors: [PipelineTestFixtures.MakeSensor("door", "boolean")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueText.Should().Be("true");
        row.ValueNumeric.Should().BeNull();
    }

    /// <summary>
    /// Датчик, отсутствующий в шаблоне, сохраняется как текст в <c>ValueText</c>
    /// (резервный путь — хранить неизвестные данные без потери).
    /// </summary>
    [Fact]
    public async Task SaveAsync_SensorNotInTemplate_FallsBackToValueText()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["unknown"] = "some_value" },
            templateSensors: []); // шаблон пустой

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ParameterName.Should().Be("unknown");
        row.ValueText.Should().Be("some_value");
        row.ValueNumeric.Should().BeNull();
    }

    /// <summary>
    /// Если значение датчика типа <c>float</c> не поддаётся парсингу как число,
    /// хендлер должен откатиться к записи в <c>ValueText</c>, а не падать с исключением.
    /// </summary>
    [Fact]
    public async Task SaveAsync_UnparseableFloat_FallsBackToValueText()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["temp"] = "N/A" },
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")]);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ValueText.Should().Be("N/A");
        row.ValueNumeric.Should().BeNull();
    }

    // =========================================================================
    // Корректность сохранённых метаданных
    // =========================================================================

    /// <summary>
    /// Поля <c>ImmId</c>, <c>ParameterName</c> и <c>Timestamp</c> должны точно
    /// соответствовать значениям из контекста; <c>Timestamp</c> конвертируется
    /// из Unix-секунд в UTC <c>DateTime</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SavesCorrectImmIdParameterNameAndTimestamp()
    {
        var immId = Guid.NewGuid();
        var unixTs = 1_700_000_000L;
        var expectedTimestamp = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;

        var context = BuildContext(
            immId: immId,
            sensors: new Dictionary<string, string> { ["temp"] = "20.0" },
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")],
            timestamp: unixTs);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ImmId.Should().Be(immId);
        row.ParameterName.Should().Be("temp");
        row.Timestamp.Should().Be(expectedTimestamp);
    }

    /// <summary>
    /// При наличии нескольких датчиков в сообщении каждый должен создать отдельную
    /// строку в таблице <c>Telemetry</c> с правильным именем параметра и значением.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MultipleSensors_SavesOneRowPerSensor()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string>
            {
                ["temp"]   = "25.0",
                ["cycles"] = "500",
                ["status"] = "running"
            },
            templateSensors:
            [
                PipelineTestFixtures.MakeSensor("temp",   "float"),
                PipelineTestFixtures.MakeSensor("cycles", "cycleCounter"),
                PipelineTestFixtures.MakeSensor("status", "string")
            ]);

        await CreateSut().SaveAsync(context);

        var rows = await _dbContext.Telemetry.ToListAsync();
        rows.Should().HaveCount(3);
        rows.Should().ContainSingle(r => r.ParameterName == "temp"   && r.ValueNumeric == 25.0m);
        rows.Should().ContainSingle(r => r.ParameterName == "cycles" && r.ValueNumeric == 500m);
        rows.Should().ContainSingle(r => r.ParameterName == "status" && r.ValueText == "running");
    }

    // =========================================================================
    // Возвращаемое значение
    // =========================================================================

    /// <summary>
    /// При успешном сохранении хотя бы одного датчика хендлер должен вернуть <c>true</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithSensors_ReturnsTrue()
    {
        var context = BuildContext(
            sensors: new Dictionary<string, string> { ["temp"] = "20.0" },
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")]);

        var result = await CreateSut().SaveAsync(context);

        result.Should().BeTrue();
    }

    /// <summary>
    /// Если словарь датчиков пуст, хендлер должен вернуть <c>false</c> и не создавать
    /// ни одной строки в таблице <c>Telemetry</c>.
    /// </summary>
    [Fact]
    public async Task SaveAsync_EmptySensors_ReturnsFalseAndSavesNothing()
    {
        var context = BuildContext(
            sensors: [],
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")]);

        var result = await CreateSut().SaveAsync(context);

        result.Should().BeFalse();
        _dbContext.Telemetry.Should().BeEmpty();
    }

    // =========================================================================
    // Вспомогательный метод
    // =========================================================================

    private static Wintime.Control.Core.DTOs.Mqtt.MqttProcessingContext BuildContext(
        Dictionary<string, string> sensors,
        IReadOnlyList<SensorTemplate> templateSensors,
        Guid? immId = null,
        long? timestamp = null)
    {
        var id = immId ?? Guid.NewGuid();
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var message  = PipelineTestFixtures.MakeMessage(id, sensors, timestamp: ts);
        var device   = PipelineTestFixtures.MakeImmDto(id);
        var template = PipelineTestFixtures.MakeTemplate(templateSensors);

        return PipelineTestFixtures.MakeContext(
            $"control/imm/{id}/telemetry", "{}", data: message, device: device, template: template);
    }
}
