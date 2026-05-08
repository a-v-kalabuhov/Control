using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;

namespace Wintime.Control.Tests.Unit.Handlers;

public class UpdateImmStatusHandlerTests
{
    private readonly IImmStatusService _statusService = Substitute.For<IImmStatusService>();
    private readonly UpdateImmStatusHandler _sut;

    public UpdateImmStatusHandlerTests()
    {
        _sut = new UpdateImmStatusHandler(_statusService, NullLogger<UpdateImmStatusHandler>.Instance);
    }

    // --- Маппинг режима в статус ---

    [Theory]
    [InlineData("auto",   "Auto")]
    [InlineData("manual", "Manual")]
    [InlineData("alarm",  "Alarm")]
    [InlineData("idle",   "Idle")]
    public async Task UpdateStatusAsync_KnownMode_MapsToCorrectStatus(string mode, string expectedStatus)
    {
        var immId = Guid.NewGuid();
        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: mode);
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1).UpdateStatusAsync(immId, expectedStatus, Arg.Any<DateTime>());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("off")]
    [InlineData("")]
    public async Task UpdateStatusAsync_UnknownMode_MapsToOffline(string mode)
    {
        var immId = Guid.NewGuid();
        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: mode);
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1).UpdateStatusAsync(immId, "Offline", Arg.Any<DateTime>());
    }

    [Fact]
    public async Task UpdateStatusAsync_NullMode_MapsToOffline()
    {
        var immId = Guid.NewGuid();
        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: null);
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1).UpdateStatusAsync(immId, "Offline", Arg.Any<DateTime>());
    }

    // --- Timestamp конвертируется корректно ---

    [Fact]
    public async Task UpdateStatusAsync_PassesCorrectTimestampToService()
    {
        var immId = Guid.NewGuid();
        var unixTs = 1_700_000_000L;
        var expectedUtc = DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;

        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: "auto", timestamp: unixTs);
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1)
            .UpdateStatusAsync(immId, "Auto", Arg.Is<DateTime>(dt => dt == expectedUtc));
    }

    // --- ImmId передаётся правильно ---

    [Fact]
    public async Task UpdateStatusAsync_PassesCorrectImmIdToService()
    {
        var immId = Guid.NewGuid();
        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: "auto");
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1)
            .UpdateStatusAsync(Arg.Is<Guid>(id => id == immId), Arg.Any<string>(), Arg.Any<DateTime>());
    }
}
