using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
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

    // --- Валидация payload ---

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
        var payload = """{"mode":"auto","sensors":{"temp":"25.0"}}""";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_MissingSensorsField_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = """{"timestamp":1700000000,"mode":"auto"}""";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, _) = await CreateSut().DecodeAsync(context);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_EmptySensorsObject_ReturnsFalse()
    {
        var immId = Guid.NewGuid();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = """{"timestamp":1700000000,"mode":"auto","sensors":{}}""";
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
        result.Data!.Timestamp.Should().Be(unixTs);
    }

    [Fact]
    public async Task DecodeAsync_IsoTimestampString_ConvertedToUnixSeconds()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var isoTime = "2023-11-14T22:13:20Z";
        var expectedUnix = new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero).ToUnixTimeSeconds();
        var topic = $"control/imm/{immId}/telemetry";
        var payload = "{\"timestamp\": \"" + isoTime + "\", \"mode\": \"auto\", \"sensors\": {\"s\": \"1\"}}";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, result) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        result.Data!.Timestamp.Should().Be(expectedUnix);
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

    private static string BuildPayload(
        long timestamp,
        string mode,
        Dictionary<string, string> sensors)
    {
        var sensorsJson = string.Join(", ", sensors.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\""));
        return $"{{\"timestamp\": {timestamp}, \"mode\": \"{mode}\", \"sensors\": {{{sensorsJson}}}}}";
    }
}
