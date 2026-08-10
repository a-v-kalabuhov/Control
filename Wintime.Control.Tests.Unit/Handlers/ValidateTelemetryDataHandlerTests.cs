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
    [InlineData("float",   "3.14")]
    [InlineData("float",   "-0.5")]
    [InlineData("int",     "42")]
    [InlineData("int",     "-7")]
    [InlineData("boolean", "true")]
    [InlineData("boolean", "false")]
    [InlineData("string",  "any text")]
    public async Task ValidateAsync_ValidSensorValue_SensorPassesThrough(string type, string value)
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var sensors = new Dictionary<string, SignalValue> { ["s1"] = new(value, Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().ContainKey("s1");
    }

    [Theory]
    [InlineData("float",   "abc")]
    [InlineData("int",     "3.14")]
    [InlineData("boolean", "yes")]
    public async Task ValidateAsync_InvalidSensorValue_SensorRemovedFromResult(string type, string value)
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0, required: false);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var sensors = new Dictionary<string, SignalValue> { ["s1"] = new(value, Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("s1");
    }

    [Fact]
    public async Task ValidateAsync_SensorWithErrorTrue_IsDropped()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", "float", threshold: 0);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var sensors = new Dictionary<string, SignalValue> { ["s1"] = new("1.23", Error: true) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("s1", "error=true — сбой чтения, сигнал не публикуется дальше");
    }

    [Fact]
    public async Task ValidateAsync_CycleCounterSensor_AlwaysValidWithoutTemplateEntry()
    {
        var immId = Guid.NewGuid();
        var template = PipelineTestFixtures.MakeTemplate([]); // cycleCounter НЕ объявлен в шаблоне
        var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("42", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["cycleCounter"].Should().Be(new SignalValue("42", false));
    }

    [Fact]
    public async Task ValidateAsync_CycleCounterWithNonIntValue_IsDropped()
    {
        var immId = Guid.NewGuid();
        var template = PipelineTestFixtures.MakeTemplate([]);
        var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("not-a-number", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);
        SetupCacheEntry(immId, context);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors.Should().NotContainKey("cycleCounter");
    }

    [Fact]
    public async Task ValidateAsync_RequiredSensorInvalid_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("s1", "float", required: true);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var sensors = new Dictionary<string, SignalValue> { ["s1"] = new("not_a_number", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
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
        var sensors = new Dictionary<string, SignalValue> { ["other_sensor"] = new("1.0", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);

        var (success, _) = await _sut.ValidateAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_SensorNotInTemplate_SensorSilentlyDropped()
    {
        var immId = Guid.NewGuid();
        var template = PipelineTestFixtures.MakeTemplate([]); // пустой шаблон
        var sensors = new Dictionary<string, SignalValue> { ["unknown"] = new("42", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
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
        var sensors = new Dictionary<string, SignalValue> { ["mode"] = new("run", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
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
        var sensors = new Dictionary<string, SignalValue> { ["mode"] = new("unknown", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
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
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("20.0", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
        var context = BuildContext(immId, message, template);

        // Кэш пуст — устройство видим впервые
        _immCache.GetEntry(immId).Returns((ImmCacheEntry?)null);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("20.0", false));
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
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("20.3", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("20.0", false), "значение в пределах порога — подставляется кэшированное");
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
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("20.7", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("20.7", false), "изменение за пределами порога — новое значение");
    }

    [Fact]
    public async Task ValidateAsync_ZeroThreshold_AlwaysPassesNewValue()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("cycle", "int", threshold: 0);
        var template = PipelineTestFixtures.MakeTemplate([sensor]);

        var cachedAt = DateTime.UtcNow.AddSeconds(-5);
        var cacheEntry = PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["cycle"] = "100" });
        _immCache.GetEntry(immId).Returns(cacheEntry);

        var messageAt = cachedAt.AddSeconds(1);
        var sensors = new Dictionary<string, SignalValue> { ["cycle"] = new("101", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["cycle"].Should().Be(new SignalValue("101", false), "Threshold=0 отключает COV — всегда проходит новое значение");
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
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("25.0", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: oldMessageTime);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("25.0", false), "out-of-order сообщение проходит без COV-фильтрации");
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
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("20.3", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("20.3", false), "первое сообщение после офлайна — порог игнорируется");
    }

    /// <summary>
    /// Сообщение, отставшее на 100 мс от закешированного, должно распознаваться как
    /// out-of-order и проходить мимо COV-фильтра. Регрессия: при обрезании метки до
    /// секунды обе величины совпадали, перестановка внутри секунды не детектировалась
    /// и значение подменялось закешированным.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_MessageOlderByMilliseconds_SkipsCovFilter()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 5);
        var template = PipelineTestFixtures.MakeTemplate([sensor], timeoutSeconds: 60);

        // Кеш хранит время более позднего сообщения — на 100 мс позже пришедшего.
        // Время берётся от текущего момента: ImmCacheEntry.IsOnline считается по
        // системным часам, и абсолютная дата в прошлом увела бы проверку в ветку
        // «офлайн», где значение тоже проходит без фильтрации, — тест перестал бы
        // различать ветки.
        var cachedAt  = DateTime.UtcNow;
        var messageAt = cachedAt.AddMilliseconds(-100);

        _immCache.GetEntry(immId).Returns(PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["temp"] = "20.0" },
            timeoutSeconds: 60));

        // Изменение в пределах порога: нормальный путь подменил бы значение на "20.0".
        var sensors = new Dictionary<string, SignalValue> { ["temp"] = new("20.3", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors, timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be(new SignalValue("20.3", false),
            "сообщение out-of-order проходит без COV-фильтрации");
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private static MqttProcessingContext BuildContext(
        Guid immId,
        MqttTelemetryMessage message,
        CachedTemplate template)
    {
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext(
            $"control/imm/{immId}/telemetry", "{}", data: message, device: device, template: template);
    }

    private void SetupCacheEntry(Guid immId, MqttProcessingContext context)
    {
        var messageAt = context.Data!.TimestampUtc;
        // Уже существующий кэш с тем же timestamp — устройство онлайн
        var entry = PipelineTestFixtures.MakeImmCacheEntry(
            immId,
            messageAt.AddSeconds(-10),
            context.Data.Sensors.ToDictionary(k => k.Key, v => v.Value.Value),
            timeoutSeconds: context.Template!.DeviceTimeoutSeconds);
        _immCache.GetEntry(immId).Returns(entry);
    }
}
