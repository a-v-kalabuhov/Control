using Wintime.Control.Emulator.Models;

namespace Wintime.Control.Emulator.Services;

public interface ISignalGenerator
{
    object GenerateValue(string mode);
}

public class FloatSignalGenerator : ISignalGenerator
{
    private readonly float _baseAuto, _baseManual, _baseIdle;
    private readonly float _variance;
    private readonly Random _random = new();

    public FloatSignalGenerator(SensorEmulationConfig cfg)
    {
        _baseAuto = cfg.BaseValueAuto;
        _baseManual = cfg.BaseValueManual;
        _baseIdle = cfg.BaseValueIdle;
        _variance = cfg.VariancePercent / 100f;
    }

    public object GenerateValue(string mode)
    {
        var baseVal = mode switch
        {
            "auto" => _baseAuto,
            "manual" => _baseManual,
            "idle" => _baseIdle,
            _ => 0
        };
        var deviation = baseVal * _variance;
        return (float)(baseVal + _random.NextDouble() * 2 * deviation - deviation);
    }
}

public class BooleanSignalGenerator : ISignalGenerator
{
    private readonly bool _valAuto, _valManual, _valIdle;
    public BooleanSignalGenerator(SensorEmulationConfig cfg)
    {
        _valAuto = cfg.ValueAuto;
        _valManual = cfg.ValueManual;
        _valIdle = cfg.ValueIdle;
    }
    public object GenerateValue(string mode) => mode switch
    {
        "auto" => _valAuto,
        "manual" => _valManual,
        "idle" => _valIdle,
        _ => false
    };
}

public class StringSignalGenerator : ISignalGenerator
{
    private readonly string _valAuto, _valManual, _valIdle;
    public StringSignalGenerator(SensorEmulationConfig cfg)
    {
        _valAuto = cfg.StringValueAuto;
        _valManual = cfg.StringValueManual;
        _valIdle = cfg.StringValueIdle;
    }
    public object GenerateValue(string mode) => mode switch
    {
        "auto" => _valAuto,
        "manual" => _valManual,
        "idle" => _valIdle,
        _ => ""
    };
}