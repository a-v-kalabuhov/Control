namespace Wintime.Control.Emulator.Models;

using FluentValidation;

/// <summary>
/// Запрос на эмуляцию IMM.
/// Используется контроллером EmulationController.
/// Т.е. настройки инстанса можно задавать HTTP запросом через API.
/// </summary>
public class EmulationRequest
{
    public string ImmId { get; set; } = "";
    public List<ProfileStep> Profile { get; set; } = new();
    public int MessagesPerMinute { get; set; } = 10;
    public List<SensorEmulationConfig> SensorConfigs { get; set; } = new();
}

/// <summary>
/// Шаг профиля эмуляции.
/// Содержит режим работы и длительность этого режима.
/// </summary>
public class ProfileStep
{
    public string Mode { get; set; } = ""; // auto, manual, idle
    public int DurationSeconds { get; set; }
}

public class SensorEmulationConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // float, boolean, string
    public float BaseValueAuto { get; set; }
    public float BaseValueManual { get; set; }
    public float BaseValueIdle { get; set; }
    public int VariancePercent { get; set; } // 0-100
    public bool ValueAuto { get; set; }
    public bool ValueManual { get; set; }
    public bool ValueIdle { get; set; }
    public string StringValueAuto { get; set; } = "";
    public string StringValueManual { get; set; } = "";
    public string StringValueIdle { get; set; } = "";
}

public class EmulationRequestValidator : AbstractValidator<EmulationRequest>
{
    public EmulationRequestValidator()
    {
        RuleFor(x => x.ImmId).NotEmpty();
        RuleFor(x => x.Profile).NotEmpty();
        RuleForEach(x => x.Profile).ChildRules(step =>
        {
            step.RuleFor(s => s.Mode).Must(m => m is "auto" or "manual" or "idle");
            step.RuleFor(s => s.DurationSeconds).GreaterThan(0);
        });
    }
}