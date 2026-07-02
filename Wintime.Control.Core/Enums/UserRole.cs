namespace Wintime.Control.Core.Enums;

public enum UserRole
{
    Admin = 0,
    Manager = 1,
    Adjuster = 2,
    Observer = 3,
    Emulator = 4,
    // Заглушка под РОСОМС (ROS-03): полноценная роль оператора появится позже.
    // Пока без UI — намеренно не выводится в дропдаун выбора роли, Мун не замечает.
    Operator = 5
}