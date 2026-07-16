using System.Net.Sockets;
using NModbus;

namespace UsrConnector.Core;

/// <summary>
/// Минимальный интерфейс чтения Modbus (только читающие FC 0x01–0x04 — инвариант read-only).
/// Значения возвращаются в host-порядке (big-endian с провода снимает реализация).
/// </summary>
public interface IModbusReader : IDisposable
{
    bool[] ReadCoils(byte unitId, ushort start, ushort count);              // FC 0x01
    bool[] ReadDiscreteInputs(byte unitId, ushort start, ushort count);     // FC 0x02
    ushort[] ReadHoldingRegisters(byte unitId, ushort start, ushort count); // FC 0x03
    ushort[] ReadInputRegisters(byte unitId, ushort start, ushort count);   // FC 0x04
}

/// <summary>Реализация поверх NModbus (Modbus TCP).</summary>
public sealed class NModbusReader : IModbusReader
{
    private readonly TcpClient _tcp;
    private readonly IModbusMaster _master;

    public NModbusReader(string host, int port, int timeoutMs)
    {
        _tcp = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
        if (!_tcp.ConnectAsync(host, port).Wait(timeoutMs))
            throw new TimeoutException($"Не удалось подключиться к {host}:{port} за {timeoutMs} мс.");

        var factory = new ModbusFactory();
        _master = factory.CreateMaster(_tcp);
        _master.Transport.ReadTimeout = timeoutMs;
        _master.Transport.WriteTimeout = timeoutMs;
        _master.Transport.Retries = 0; // ретраи/offline — уровнем выше (ConnectorEngine + автомат)
    }

    public bool[] ReadCoils(byte unitId, ushort start, ushort count) =>
        _master.ReadCoils(unitId, start, count);

    public bool[] ReadDiscreteInputs(byte unitId, ushort start, ushort count) =>
        _master.ReadInputs(unitId, start, count);

    public ushort[] ReadHoldingRegisters(byte unitId, ushort start, ushort count) =>
        _master.ReadHoldingRegisters(unitId, start, count);

    public ushort[] ReadInputRegisters(byte unitId, ushort start, ushort count) =>
        _master.ReadInputRegisters(unitId, start, count);

    public void Dispose()
    {
        _master.Dispose();
        _tcp.Dispose();
    }
}
