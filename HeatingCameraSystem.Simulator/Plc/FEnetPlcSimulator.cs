using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Memory;
using HeatingCameraSystem.Simulator.State;
using VagabondK.Protocols.Channels;
using VagabondK.Protocols.Logging;
using VagabondK.Protocols.LSElectric;
using VagabondK.Protocols.LSElectric.FEnet;
using VagabondK.Protocols.LSElectric.FEnet.Simulation;

namespace HeatingCameraSystem.Simulator.Plc;

public sealed class FEnetPlcSimulator : IPlcSimulatorEndpoint
{
    private readonly SimulatorSettings _settings;
    private readonly PlcSettings _plc;
    private readonly SimulatorState _state;
    private readonly FEnetDeviceMemory _memory;
    private TcpChannelProvider? _provider;
    private FEnetSimulationService? _service;
    private PlcDynamicsEngine? _dynamics;

    public FEnetPlcSimulator(SimulatorSettings settings, PlcSettings? plc = null, SimulatorState? state = null)
    {
        _settings = settings;
        _plc = plc ?? new PlcSettings { IpAddress = settings.Endpoint.ListenAddress, Port = settings.Endpoint.ListenPort };
        _state = state ?? new SimulatorState(settings.Cameras.Select(c => c.AgentId));
        _memory = new FEnetDeviceMemory(_plc.UseHexBitIndex);
        InitializeDefaults();
    }

    public FEnetDeviceMemory Memory => _memory;

    public void Start()
    {
        if (_provider != null) return;

        var ip = IPAddress.Parse(_settings.Endpoint.ListenAddress);
        _provider = new TcpChannelProvider(ip, _settings.Endpoint.ListenPort) { Logger = new NullChannelLogger() };
        _service = new FEnetSimulationService(_provider) { UseHexBitIndex = _plc.UseHexBitIndex };
        _service.RequestedReadIndividual += OnReadIndividual;
        _service.RequestedWriteIndividual += OnWriteIndividual;
        _service.RequestedReadContinuous += OnReadContinuous;
        _service.RequestedWriteContinuous += OnWriteContinuous;
        _provider.Start();
        _state.SetPlcOnline(true);
        _dynamics = new PlcDynamicsEngine(_memory, _plc, _settings.Dynamics);
        _dynamics.Start();
    }

    public void Stop()
    {
        _state.SetPlcOnline(false);
        _dynamics?.Dispose();
        _dynamics = null;
        _service?.Dispose();
        _service = null;
        _provider?.Dispose();
        _provider = null;
    }

    public void Dispose() => Stop();

    private void InitializeDefaults()
    {
        WriteScaled(_plc.TempPv, 25.0f, 10);
        WriteScaled(_plc.TempSv, 25.0f, 10);
        WriteScaled(_plc.HumPv, 50.0f, 10);
        WriteScaled(_plc.HumSv, 50.0f, 10);
        WriteScaled(_plc.Bb1Pv, 25.0f, 10);
        WriteScaled(_plc.Bb1Sv, 25.0f, 10);
        WriteScaled(_plc.Bb2Pv, 25.0f, 10);
        WriteScaled(_plc.Bb2Sv, 25.0f, 10);
        _memory.WriteWordToken(_plc.ServoCurrentPoint, 0);
        _memory.WriteBitToken(_plc.ServoXHomeBit, true);
        _memory.WriteBitToken(_plc.ServoYHomeBit, true);
    }

    private void WriteScaled(string token, float value, int scale) =>
        _memory.WriteWordToken(token, (short)Math.Round(value * scale));

    private void OnReadIndividual(object? sender, FEnetRequestedReadIndividualEventArgs e) => Guard(() => _memory.ReadIndividual(e.ResponseValues), e);

    private void OnWriteIndividual(object? sender, FEnetRequestedWriteIndividualEventArgs e) => Guard(() => _memory.WriteIndividual(e.Values), e);

    private void OnReadContinuous(object? sender, FEnetRequestedReadContinuousEventArgs e)
    {
        Guard(() =>
        {
            int offset = FEnetDeviceMemory.ByteOffsetOf(e.StartDeviceVariable);
            e.ResponseValues = _memory.ReadContinuous(e.StartDeviceVariable.DeviceType, offset, e.Count);
        }, e);
    }

    private void OnWriteContinuous(object? sender, FEnetRequestedWriteContinuousEventArgs e)
    {
        Guard(() =>
        {
            int offset = FEnetDeviceMemory.ByteOffsetOf(e.StartDeviceVariable);
            _memory.WriteContinuous(e.StartDeviceVariable.DeviceType, offset, e.Values.ToArray());
        }, e);
    }

    private static void Guard(Action action, FEnetRequestedEventArgs e)
    {
        try
        {
            action();
        }
        catch (DeviceMemoryException ex)
        {
            e.NAKCode = ex.Message.Contains("Unsupported", StringComparison.OrdinalIgnoreCase)
                ? FEnetNAKCode.IlegalDeviceMemory
                : FEnetNAKCode.OutOfRangeDeviceVariable;
        }
    }

    private sealed class NullChannelLogger : IChannelLogger
    {
        public void Log(ChannelLog log) { }
    }
}
