using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class DecodeTelemetryDataHandlerTests : IDisposable
{
    private readonly ControlDbContext _dbContext;
    private readonly ITemplateCache _templateCache = Substitute.For<ITemplateCache>();

    public DecodeTelemetryDataHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()) // уникальная БД на каждый тест
            .Options;
        _dbContext = new ControlDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private DecodeTelemetryDataHandler CreateSut()
        => new(_dbContext, _templateCache, NullLogger<DecodeTelemetryDataHandler>.Instance);

    // --- Успешный сценарий ---

    [Fact]
    public async Task DecodeAsync_ValidTopicAndPayload_ReturnsSuccessWithPopulatedContext()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        var template = PipelineTestFixtures.MakeTemplate(timeoutSeconds: 60);
        _templateCache.GetById(templateId).Returns(template);

        var topic = $"control/imm/{immId}/telemetry";
        var payload = BuildPayload(1_700_000_000, "auto", new Dictionary<string, string> { ["temp"] = "25.0" });
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, result) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        result.Device.Should().NotBeNull();
        result.Device!.Id.Should().Be(immId);
        result.Template.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data!.Sensors.Should().ContainKey("temp");
        result.Data.Sensors.Should().ContainKey("cycleCounter");
    }

    // --- Валидация топика ---

    [Theory]
    [InlineData("control/imm/not-a-guid/telemetry")]
    [InlineData("control/imm/telemetry")]
    [InlineData("wrong/imm/guid-here/telemetry")]
    [InlineData("control/imm/guid-here/events")]
    public async Task DecodeAsync_InvalidTopic_ReturnsFalse(string topic)
    {
        var payload = BuildPayload(1_700_000_000, "auto", new Dictionary<string, string> { ["s"] = "1" });
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    // --- Валидация payload (базовая, до v2 sensors/cycle контракта) ---

    [Fact]
    public async Task DecodeAsync_InvalidJson_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var context = PipelineTestFixtures.MakeContext(topic, "not json at all");

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_MissingTimestampField_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = """{"mode":"auto","sensors":{"cycleCounter":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_EmptySensorsObject_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = """{"timestamp":1700000000,"mode":"auto","sensors":{},"currentCycle":null,"lastCycle":null}""";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    // --- Устройство и шаблон ---

    [Fact]
    public async Task DecodeAsync_DeviceNotInDatabase_ReturnsFalse()
    {
        // IMM не добавляем в БД
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = BuildPayload(1_700_000_000, "auto", new Dictionary<string, string> { ["s"] = "1" });
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_TemplateNotInCache_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);

        // Шаблон не возвращается из кэша
        _templateCache.GetById(templateId).Returns((CachedTemplate?)null);

        var topic = $"control/imm/{immId}/telemetry";
        var payload = BuildPayload(1_700_000_000, "auto", new Dictionary<string, string> { ["s"] = "1" });
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    // --- Timestamp нормализация ---

    [Fact]
    public async Task DecodeAsync_UnixTimestampNumber_PreservedInResult()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var unixTs = 1_700_000_000L;
        var topic = $"control/imm/{immId}/telemetry";
        var payload = BuildPayload(unixTs, "auto", new Dictionary<string, string> { ["s"] = "1" });
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, result) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        result.Data!.TimestampUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime);
        result.Data.TimestampUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("2023-11-14T22:13:20.1230000Z", "2023-11-14T22:13:20.123")]
    [InlineData("2023-11-14T22:13:20.1230000+03:00", "2023-11-14T19:13:20.123")]
    [InlineData("2023-11-14T22:13:20.1230000", "2023-11-14T22:13:20.123")]
    public async Task DecodeAsync_IsoTimestampAllThreeForms_NormalizesToUtc(string isoTime, string expectedUtc)
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var expected = DateTime.SpecifyKind(DateTime.Parse(expectedUtc, CultureInfo.InvariantCulture), DateTimeKind.Utc);
        var topic = $"control/imm/{immId}/telemetry";
        var payload = "{\"timestamp\": \"" + isoTime + "\", \"mode\": \"auto\", \"sensors\": {\"cycleCounter\":{\"value\":\"1\",\"error\":false}}, \"currentCycle\": null, \"lastCycle\": null}";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, result) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        result.Data!.TimestampUtc.Should().Be(expected);
        result.Data.TimestampUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DecodeAsync_TwoIsoTimestampsInSameSecond_ProduceDistinctValues()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var topic = $"control/imm/{immId}/telemetry";
        var first  = "{\"timestamp\": \"2023-11-14T22:13:20.1000000Z\", \"mode\": \"auto\", \"sensors\": {\"cycleCounter\":{\"value\":\"1\",\"error\":false}}, \"currentCycle\": null, \"lastCycle\": null}";
        var second = "{\"timestamp\": \"2023-11-14T22:13:20.2000000Z\", \"mode\": \"auto\", \"sensors\": {\"cycleCounter\":{\"value\":\"1\",\"error\":false}}, \"currentCycle\": null, \"lastCycle\": null}";

        var (ok1, r1) = await CreateSut().DecodeAsync(PipelineTestFixtures.MakeContext(topic, first));
        var (ok2, r2) = await CreateSut().DecodeAsync(PipelineTestFixtures.MakeContext(topic, second));

        ok1.Should().BeTrue();
        ok2.Should().BeTrue();
        r2.Data!.TimestampUtc.Should().BeAfter(r1.Data!.TimestampUtc,
            "два сообщения внутри одной секунды обязаны различаться по времени");
    }

    // =========================================================================
    // Контракт v2: sensors как {value, error}, currentCycle/lastCycle
    // =========================================================================

    [Fact]
    public async Task DecodeAsync_FullV2Payload_ParsesSensorsAndBothCycleBlocks()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var payload = $$"""
            {
              "timestamp": "2026-08-09T12:34:56.789Z",
              "mode": "auto",
              "sensors": {
                "cycleCounter": { "value": "42", "error": false },
                "matTemp1": { "value": "215.3", "error": false }
              },
              "currentCycle": {
                "number": 42,
                "startTime": "2026-08-09T12:34:50.000Z",
                "injectionStartTime": "2026-08-09T12:34:50.800Z",
                "cushion": 5.2
              },
              "lastCycle": {
                "number": 41,
                "startTime": "2026-08-09T12:34:35.000Z",
                "endTime": "2026-08-09T12:34:50.000Z",
                "injectionStartTime": "2026-08-09T12:34:35.800Z",
                "cushion": 5.1
              }
            }
            """;
        var topic = $"control/imm/{immId}/telemetry";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, updated) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        updated.Data!.Sensors["cycleCounter"].Should().Be(new SignalValue("42", false));
        updated.Data.Sensors["matTemp1"].Should().Be(new SignalValue("215.3", false));
        updated.Data.CurrentCycle.Should().Be(new CycleSnapshot(42,
            new DateTime(2026, 8, 9, 12, 34, 50, DateTimeKind.Utc), new DateTime(2026, 8, 9, 12, 34, 50, 800, DateTimeKind.Utc), 5.2m));
        updated.Data.LastCycle.Should().Be(new CompletedCycleSnapshot(41,
            new DateTime(2026, 8, 9, 12, 34, 35, DateTimeKind.Utc), new DateTime(2026, 8, 9, 12, 34, 50, DateTimeKind.Utc),
            new DateTime(2026, 8, 9, 12, 34, 35, 800, DateTimeKind.Utc), 5.1m));
    }

    [Fact]
    public async Task DecodeAsync_NullCycleBlocks_ParsesAsNull()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var payload = """
            {
              "timestamp": "2026-08-09T12:34:56.789Z",
              "mode": "idle",
              "sensors": { "cycleCounter": { "value": "0", "error": false } },
              "currentCycle": null,
              "lastCycle": null
            }
            """;
        var topic = $"control/imm/{immId}/telemetry";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, updated) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        updated.Data!.CurrentCycle.Should().BeNull();
        updated.Data.LastCycle.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","sensors":{"cycleCounter":{"value":"1","error":false}}}""")] // no currentCycle/lastCycle keys
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","currentCycle":null,"lastCycle":null}""")] // no sensors
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","sensors":{"matTemp1":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // sensors missing cycleCounter
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"offline","sensors":{"cycleCounter":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // offline never appears on wire — unknown to contract
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"standby","sensors":{"cycleCounter":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // unknown mode
    public async Task DecodeAsync_MissingRequiredSectionOrInvalidMode_Rejected(string payload)
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var topic = $"control/imm/{immId}/telemetry";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_SensorWithErrorTrue_PreservesErrorFlag()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var payload = """
            {
              "timestamp": "2026-08-09T12:00:00Z",
              "mode": "auto",
              "sensors": {
                "cycleCounter": { "value": "1", "error": false },
                "doorSensor": { "value": "0", "error": true }
              },
              "currentCycle": null,
              "lastCycle": null
            }
            """;
        var topic = $"control/imm/{immId}/telemetry";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, updated) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        updated.Data!.Sensors["doorSensor"].Error.Should().BeTrue();
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private async System.Threading.Tasks.Task SeedImm(Guid immId, Guid templateId)
    {
        _dbContext.Imms.Add(new Imm
        {
            Id = immId,
            Name = "Test IMM",
            TemplateId = templateId,
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Строит валидный v2 payload: sensors как {value, error}-объекты (с автодобавлением
    /// обязательного cycleCounter), currentCycle/lastCycle как null.
    /// </summary>
    private static string BuildPayload(
        long timestamp,
        string mode,
        Dictionary<string, string> sensors)
    {
        var sensorsWithCounter = new Dictionary<string, string>(sensors);
        if (!sensorsWithCounter.ContainsKey("cycleCounter"))
            sensorsWithCounter["cycleCounter"] = "0";

        var sensorsJson = string.Join(", ", sensorsWithCounter.Select(kv =>
            $"\"{kv.Key}\": {{ \"value\": \"{kv.Value}\", \"error\": false }}"));
        return $"{{\"timestamp\": {timestamp}, \"mode\": \"{mode}\", \"sensors\": {{{sensorsJson}}}, \"currentCycle\": null, \"lastCycle\": null}}";
    }
}
