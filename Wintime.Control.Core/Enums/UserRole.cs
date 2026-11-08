namespace Wintime.Control.Core.Enums;

public enum UserRole
{
    Admin = 0,
    Manager = 1,
    Adjuster = 2,
    Observer = 3,
    Emulator = 4,
    // Литейщик (PZP-30): только дашборд и карточка ТПА в режиме чтения.
    Operator = 5,
    ShiftLead = 6
}