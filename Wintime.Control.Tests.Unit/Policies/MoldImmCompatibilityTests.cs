using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class MoldImmCompatibilityTests
{
    [Fact]
    public void IsCompatible_EmptyList_AnyImmIsCompatible()
    {
        MoldImmCompatibility.IsCompatible(Array.Empty<Guid>(), Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void IsCompatible_ImmInList_True()
    {
        var imm = Guid.NewGuid();
        MoldImmCompatibility.IsCompatible(new[] { Guid.NewGuid(), imm }, imm).Should().BeTrue();
    }

    [Fact]
    public void IsCompatible_ImmNotInList_False()
    {
        MoldImmCompatibility.IsCompatible(new[] { Guid.NewGuid() }, Guid.NewGuid()).Should().BeFalse();
    }
}
