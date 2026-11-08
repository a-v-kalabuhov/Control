namespace Wintime.Control.Shared.Constants;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Adjuster = "Adjuster";
    public const string Observer = "Observer";
    public const string Emulator = "Emulator";
    public const string Operator = "Operator";
    public const string ShiftLead = "ShiftLead";

    public static readonly string[] All = { Admin, Manager, Adjuster, Observer, Emulator, Operator, ShiftLead };
}