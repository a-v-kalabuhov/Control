using Wintime.Control.Emulator.Models;

namespace Wintime.Control.Emulator.Services;

/// <summary>
/// Интерфейс генератора значений для сигналов.
/// Генерирует значения в зависимости от режима работы.
/// Нужен для эмуляции датчиков оборудования (IMM).
/// </summary>
public interface ISignalGenerator
{
    object GenerateValue(string mode);
}

/// <summary>
/// Генератор значений типа float.
/// Выдаёт случайные значения в диапазоне от базового значения с учётом вариации.
/// Для каждого из доступных режимов работы оборудования (авто, ручной, пауза), используется своё базовое значение.
/// </summary>
public class FloatSignalGenerator : ISignalGenerator
{
    private readonly float _baseAuto, _baseManual, _baseIdle, _baseAlarm;
    private readonly float _variance;
    private readonly Random _random = new();

    public FloatSignalGenerator(SensorEmulationConfig cfg)
    {
        _baseAuto = cfg.BaseValueAuto;
        _baseManual = cfg.BaseValueManual;
        _baseIdle = cfg.BaseValueIdle;
        _baseAlarm = cfg.BaseValueAlarm;
        _variance = cfg.VariancePercent / 100f;
    }

    public object GenerateValue(string mode)
    {
        var baseVal = mode switch
        {
            "auto" => _baseAuto,
            "manual" => _baseManual,
            "idle" => _baseIdle,
            "alarm" => _baseAlarm,
            _ => 0
        };
        var deviation = baseVal * _variance;
        return (float)(baseVal + _random.NextDouble() * 2 * deviation - deviation);
    }
}

/// <summary>
/// Генератор значений типа int.
/// Выдаёт случайные целочисленные значения в диапазоне от базового значения с учётом вариации.
/// Для каждого из доступных режимов работы оборудования (авто, ручной, пауза), используется своё базовое значение.
/// </summary>
public class IntSignalGenerator : ISignalGenerator
{
    private readonly int _baseAuto, _baseManual, _baseIdle, _baseAlarm;
    private readonly float _variance;
    private readonly Random _random = new();

    public IntSignalGenerator(SensorEmulationConfig cfg)
    {
        _baseAuto = cfg.IntBaseValueAuto;
        _baseManual = cfg.IntBaseValueManual;
        _baseIdle = cfg.IntBaseValueIdle;
        _baseAlarm = cfg.IntBaseValueAlarm;
        _variance = cfg.VariancePercent / 100f;
    }

    public object GenerateValue(string mode)
    {
        var baseVal = mode switch
        {
            "auto" => _baseAuto,
            "manual" => _baseManual,
            "idle" => _baseIdle,
            "alarm" => _baseAlarm,
            _ => 0
        };
        var deviation = (int)Math.Round(baseVal * _variance);
        return baseVal + _random.Next(-deviation, deviation + 1);
    }
}

/// <summary>
/// Генератор значений типа bool.
/// Возвращает значение, соответствующее режиму работы.
/// Значения задаются в конфигурации.
/// </summary>
public class BooleanSignalGenerator : ISignalGenerator
{
    private readonly bool _valAuto, _valManual, _valIdle, _valAlarm;
    public BooleanSignalGenerator(SensorEmulationConfig cfg)
    {
        _valAuto = cfg.ValueAuto;
        _valManual = cfg.ValueManual;
        _valIdle = cfg.ValueIdle;
        _valAlarm = cfg.ValueAlarm;
    }
    public object GenerateValue(string mode) => mode switch
    {
        "auto" => _valAuto,
        "manual" => _valManual,
        "idle" => _valIdle,
        "alarm" => _valAlarm,
        _ => false
    };
}

/// <summary>
/// Генератор значений типа string.
/// Возвращает значение, соответствующее режиму работы.
/// Значения задаются в конфигурации.
/// </summary>
public class StringSignalGenerator : ISignalGenerator
{
    private readonly string _valAuto, _valManual, _valIdle, _valAlarm;
    public StringSignalGenerator(SensorEmulationConfig cfg)
    {
        _valAuto = cfg.StringValueAuto;
        _valManual = cfg.StringValueManual;
        _valIdle = cfg.StringValueIdle;
        _valAlarm = cfg.StringValueAlarm;
    }
    public object GenerateValue(string mode) => mode switch
    {
        "auto" => _valAuto,
        "manual" => _valManual,
        "idle" => _valIdle,
         "alarm" => _valAlarm,
        _ => ""
    };
}