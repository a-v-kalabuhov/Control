using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;

namespace Wintime.Control.Tests.Unit.Handlers;

public class ValidateTelemetryDataHandlerTests
{
    private readonly IImmCache _immCache = Substitute.For<IImmCache>();
    private readonly ValidateTelemetryDataHandler _sut;

    public ValidateTelemetryDataHandlerTests()
    {
        _sut = new ValidateTelemetryDataHandler(_immCache, NullLogger<ValidateTelemetryDataHandler>.Instance);
    }

    // =========================================================================
    // Часть 1: Валидация типов
    // =========================================================================

    [Theory]
    [InlineData("float",        "3.14")]
    [InlineData("float",        "-0.5")]
    [InlineData("int",          "42")]
    [InlineData("int",          "-7")]
    [InlineData("boolean",      "true")]
    [InlineData("boolean",      "false")]
    [InlineData("cycleCounter", "100")]
    [InlineData("string",       "any text")]
    public async Task ValidateAsync_ValidSensorValue_SensorPassesThrough(string type, string value)
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["s1"] = value });
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().ContainKey("s1");
    }

    [Theory]
    [InlineData("float",        "abc")]
    [InlineData("int",          "3.14")]
    [InlineData("boolean",      "yes")]
    [InlineData("cycleCounter", "one")]
    public async Task ValidateAsync_InvalidSensorValue_SensorRemovedFromResult(string type, string value)
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0, required: false);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["s1"] = value });
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("s1");
    }

    [Fact]
    public async Task ValidateAsync_RequiredSensorInvalid_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", "float", required: true);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["s1"] = "not_a_number" });
        var context = BuildContext(immId, message, template);

        var (success, _) = await _sut.ValidateAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_RequiredSensorMissing_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("required_sensor", "float", required: true);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["other_sensor"] = "1.0" });
        var context = BuildContext(immId, message, template);

        var (success, _) = await _sut.ValidateAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_SensorNotInTemplate_SensorSilentlyDropped()
    {
        var immId = Guid.NewGuid();
        var template = PipelineTestFixtures.MakeTemplate([]); // пустой шаблон
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["unknown"] = "42" });
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("unknown");
    }

    [Fact]
    public async Task ValidateAsync_AllowedValues_ValueInListPasses()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("mode", "string", allowedValues: ["run", "stop", "idle"]);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["mode"] = "run" });
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().ContainKey("mode");
    }

    [Fact]
    public async Task ValidateAsync_AllowedValues_ValueNotInListDropped()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("mode", "string", allowedValues: ["run", "stop"]);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["mode"] = "unknown" });
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("mode");
    }

    // =========================================================================
    // Часть 2: COV-фильтрация
    // =========================================================================

    [Fact]
    public async Task ValidateAsync_FirstMessageFromDevice_AllSensorsPassThrough()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 0.5m);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["temp"] = "20.0" });
        var context = BuildContext(immId, message, template);

        // Кэш пуст — устройство видим впервые
        _immCache.GetEntry(immId).Returns((ImmCacheEntry?)null);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("20.0");
        _immCache.Received(1).AddImm(immId, Arg.Any<int>());
        _immCache.Received(1).UpdateEntry(immId, Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task ValidateAsync_ChangeWithinThreshold_CachedValueSubstituted()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 0.5m);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);

        var cachedAt = DateTime.UtcNow.AddSeconds(-5);
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["temp"] = "20.0" });
        _immCache.GetEntry(immId).Returns(cacheEntry);

        // Новое значение: 20.3 — изменение 0.3, порог 0.5 → COV не срабатывает
        var messageAt = cachedAt.AddSeconds(1);
        var unixTs = new DateTimeOffset(messageAt).ToUnixTimeSeconds();
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "20.3" },
            timestamp: unixTs);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("20.0", "значение в пределах порога — подставляется кэшированное");
    }

    [Fact]
    public async Task ValidateAsync_ChangeBeyondThreshold_NewValueStored()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 0.5m);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);

        var cachedAt = DateTime.UtcNow.AddSeconds(-5);
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["temp"] = "20.0" });
        _immCache.GetEntry(immId).Returns(cacheEntry);

        // Новое значение: 20.7 — изменение 0.7, порог 0.5 → COV срабатывает
        var messageAt = cachedAt.AddSeconds(1);
        var unixTs = new DateTimeOffset(messageAt).ToUnixTimeSeconds();
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "20.7" },
            timestamp: unixTs);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("20.7", "изменение за пределами порога — новое значение");
    }

    [Fact]
    public async Task ValidateAsync_ZeroThreshold_AlwaysPassesNewValue()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("cycle", "cycleCounter", threshold: 0);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);

        var cachedAt = DateTime.UtcNow.AddSeconds(-5);
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["cycle"] = "100" });
        _immCache.GetEntry(immId).Returns(cacheEntry);

        var messageAt = cachedAt.AddSeconds(1);
        var unixTs = new DateTimeOffset(messageAt).ToUnixTimeSeconds();
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["cycle"] = "101" },
            timestamp: unixTs);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["cycle"].Should().Be("101", "Threshold=0 отключает COV — всегда проходит новое значение");
    }

    [Fact]
    public async Task ValidateAsync_OutOfOrderMessage_PassesThroughWithoutFiltering()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 0.5m);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);

        // Кэш содержит более поздний timestamp, чем в новом сообщении
        var cacheTime = DateTime.UtcNow;
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, cacheTime,
            new Dictionary<string, string> { ["temp"] = "20.0" });
        _immCache.GetEntry(immId).Returns(cacheEntry);

        var oldMessageTime = cacheTime.AddSeconds(-10); // старше кэша
        var unixTs = new DateTimeOffset(oldMessageTime).ToUnixTimeSeconds();
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "25.0" },
            timestamp: unixTs);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("25.0", "out-of-order сообщение проходит без COV-фильтрации");
    }

    [Fact]
    public async Task ValidateAsync_DeviceWasOffline_AllSensorsTreatedAsChanged()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 0.5m);
        // DeviceTimeoutSeconds = 60, LastMessageAt давно → IsOnline = false
        var template = PipelineTestFixtures.MakeTemplate([sensor], timeoutSeconds: 60);

        var offlineTime = DateTime.UtcNow.AddSeconds(-120); // offline: 120 > 60 сек
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, offlineTime,
            new Dictionary<string, string> { ["temp"] = "20.0" },
            timeoutSeconds: 60);
        _immCache.GetEntry(immId).Returns(cacheEntry);

        // Новое значение в пределах порога, но устройство было offline
        var messageAt = DateTime.UtcNow;
        var unixTs = new DateTimeOffset(messageAt).ToUnixTimeSeconds();
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "20.3" },
            timestamp: unixTs);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("20.3", "первое сообщение после офлайна — порог игнорируется");
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private static MqttProcessingContext BuildContext(
        Guid immId,
        Wintime.Control.Core.DTOs.Mqtt.MqttTelemetryMessage message,
        Wintime.Control.Core.Cache.CachedTemplate template)
    {
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext(
            $"control/imm/{immId}/telemetry", "{}", data: message, device: device, template: template);
    }

    private void SetupCacheEntry(Guid immId, MqttProcessingContext context)
    {
        var messageAt = DateTimeOffset.FromUnixTimeSeconds(context.Data!.Timestamp).UtcDateTime;
        // Уже существующий кэш с тем же timestamp — устройство онлайн
        var entry = PipelineTestFixtures.MakeImmCacheEntry(
            immId,
            messageAt.AddSeconds(-10),
            context.Data.Sensors.ToDictionary(k => k.Key, v => v.Value),
            timeoutSeconds: context.Template!.DeviceTimeoutSeconds);
        _immCache.GetEntry(immId).Returns(entry);
    }
}
