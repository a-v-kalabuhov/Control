using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Shared.Constants;
using Xunit;

namespace Wintime.Control.Tests.Unit.Enums;

public class ShiftLeadRoleTests
{
    [Fact]
    public void ShiftLead_has_stable_enum_value_and_shared_name()
    {
        ((int)UserRole.ShiftLead).Should().Be(6);
        UserRole.ShiftLead.ToString().Should().Be(Roles.ShiftLead);
        Roles.All.Should().Contain(Roles.ShiftLead);
    }

    [Fact]
    public void ShiftLead_is_not_adjuster_manager_or_admin()
    {
        Roles.ShiftLead.Should().NotBe(Roles.Adjuster);
        Roles.ShiftLead.Should().NotBe(Roles.Manager);
        Roles.ShiftLead.Should().NotBe(Roles.Admin);
    }
}
