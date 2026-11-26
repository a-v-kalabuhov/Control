using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>Проверяет актуальную роль, а не только неизменяемый role claim старого JWT.</summary>
public static class ShiftLeadAccessPolicy
{
    public static void EnsureCurrentRole(string userId, ShiftLeadFallbackSnapshot snapshot)
    {
        if (!snapshot.Users.TryGetValue(userId, out var user) ||
            !user.IsActive || user.Role != UserRole.ShiftLead)
            throw new ShiftAccessException("Персональный доступ требует активной роли старшего смены");
    }

    public static void EnsureCurrentRole(StaffingCaller caller, ShiftLeadFallbackSnapshot snapshot)
    {
        if (caller.IsShiftLead)
        {
            EnsureCurrentRole(caller.UserId, snapshot);
            return;
        }
        if (!caller.IsManagerOrAdmin) return;

        if (!snapshot.Users.TryGetValue(caller.UserId, out var user) || !user.IsActive ||
            (user.Role != UserRole.Manager && user.Role != UserRole.Admin))
            throw new ShiftAccessException("Доступ требует актуальной активной роли руководителя или администратора");
    }
}
