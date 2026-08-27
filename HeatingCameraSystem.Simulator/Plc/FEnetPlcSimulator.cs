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

/// <summary>
/// LS XGT FEnet 서버 엔드포인트 에뮬레이터(기본 <c>127.0.0.1:2004</c>). VagabondK
/// <c>FEnetSimulationService</c>의 개별/연속 읽기·쓰기 요청을 <see cref="FEnetDeviceMemory"/>
/// (D/M/P/K/L/F 영역, XGB <c>UseHexBitIndex</c> 규칙)로 처리하므로 실 PlcXgtClient가 그대로 붙는다.
/// 물리 거동은 <see cref="PlcDynamicsEngine"/>이 담당한다 — 실 챔버와 달리 SV를 향한 고정 속도의
/// 선형 램프일 뿐, 열 관성·오버슈트·외란은 전혀 모델링하지 않는다. 시뮬레이션 결과를 읽을 때
/// 이 차이를 전제로 해야 한다.
/// </summary>
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

    /// <summary>테스트가 디바이스 값을 직접 넣고 확인할 수 있게 노출한 저장소.</summary>
    public FEnetDeviceMemory Memory => _memory;

    /// <summary>TCP 리슨을 열고 이벤트 핸들러를 붙인 뒤 동역학 엔진을 기동한다. 이미 켜져 있으면 아무 것도 안 한다.</summary>
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

    /// <summary>리슨/서비스/동역학을 모두 내린다. 메모리 내용은 유지되므로 재기동 시 값이 이어진다.</summary>
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

    // 상온 대기 상태를 심는다: 온도 25℃/습도 50%RH(×10), 흑체 25℃(×100), 원점 복귀 완료.
    private void InitializeDefaults()
    {
        WriteScaled(_plc.TempPv, 25.0f, 10);
        WriteScaled(_plc.TempSv, 25.0f, 10);
        WriteScaled(_plc.TempTarget, 25.0f, 10);
        WriteScaled(_plc.HumPv, 50.0f, 10);
        WriteScaled(_plc.HumSv, 50.0f, 10);
        WriteScaled(_plc.Bb1Pv, 25.0f, 100);
        WriteScaled(_plc.Bb1Sv, 25.0f, 100);
        WriteScaled(_plc.Bb2Pv, 25.0f, 100);
        WriteScaled(_plc.Bb2Sv, 25.0f, 100);
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

    // 메모리 예외를 FEnet NAK 코드로 바꾼다: 미지원 영역 → IlegalDeviceMemory, 범위 밖 → OutOfRangeDeviceVariable.
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
