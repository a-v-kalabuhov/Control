using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

public class UnplannedRunPersistenceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public UnplannedRunPersistenceTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task UnplannedRun_persists_and_reads_back_with_utc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var run = new UnplannedRun { ImmId = _factory.TestImmId, StartTime = DateTime.UtcNow };
        db.UnplannedRuns.Add(run);
        await db.SaveChangesAsync();

        var loaded = await db.UnplannedRuns.FindAsync(run.Id);
        loaded.Should().NotBeNull();
        loaded!.ClosedAt.Should().BeNull();
    }
}
