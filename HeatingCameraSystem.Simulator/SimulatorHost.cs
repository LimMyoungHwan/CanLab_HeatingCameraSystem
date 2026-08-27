using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Plc;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Simulator;

/// <summary>PLC 시뮬레이터 엔드포인트 추상. 콘솔 명령 <c>plc online|offline</c>이 Start/Stop을 호출한다.</summary>
public interface IPlcSimulatorEndpoint : IDisposable
{
    void Start();
    void Stop();
}

/// <summary>NATS 카메라 Agent 엔드포인트 추상. <c>--plc-only</c> 모드에서는 생성하지 않는다.</summary>
public interface ICameraAgentEndpoint : IAsyncDisposable
{
    Task StartAsync();
}

/// <summary>
/// Simulator 최상위 호스트. FEnet PLC 엔드포인트와 NATS 카메라 Agent를 함께 기동하고,
/// 표준 입력의 대화형 명령(status / plc online·offline / plc fault / camera 모드 전환)으로
/// 실행 중 장애 주입을 지원한다. 팩토리·입출력 주입은 테스트 대체용이다.
/// </summary>
public sealed class SimulatorHost : IAsyncDisposable
{
    private readonly SimulatorSettings _settings;
    private readonly SimulatorState _state;
    private readonly Func<SimulatorSettings, SimulatorState, IPlcSimulatorEndpoint> _plcFactory;
    private readonly Func<SimulatorSettings, SimulatorState, ICameraAgentEndpoint> _cameraFactory;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly bool _startCameras;
    private IPlcSimulatorEndpoint? _plc;
    private ICameraAgentEndpoint? _cameras;

    public SimulatorHost(
        SimulatorSettings settings,
        SimulatorState? state = null,
        TextReader? input = null,
        TextWriter? output = null,
        Func<SimulatorSettings, SimulatorState, IPlcSimulatorEndpoint>? plcFactory = null,
        Func<SimulatorSettings, SimulatorState, ICameraAgentEndpoint>? cameraFactory = null,
        bool startCameras = true)
    {
        _settings = settings;
        _state = state ?? new SimulatorState(settings.Cameras.Select(c => c.AgentId));
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
        _plcFactory = plcFactory ?? ((s, state) => new FEnetPlcSimulator(s, state: state));
        _cameraFactory = cameraFactory ?? ((s, state) => new NatsCameraAgentSimulator(s, state));
        _startCameras = startCameras;
    }

    /// <summary>
    /// PLC를 먼저 올리고(카메라 생략 가능), 준비 완료를 <c>SIMULATOR READY ...</c> 한 줄로 출력한다.
    /// 실행 스크립트가 이 줄을 기동 신호로 읽는다.
    /// </summary>
    public async Task StartAsync()
    {
        _plc = _plcFactory(_settings, _state);
        _plc.Start();
        _state.SetPlcOnline(true);
        int cameraCount = 0;
        if (_startCameras)
        {
            _cameras = _cameraFactory(_settings, _state);
            await _cameras.StartAsync().ConfigureAwait(false);
            cameraCount = _settings.Cameras.Count;
        }
        await _output.WriteLineAsync($"SIMULATOR READY plc={_settings.Endpoint.ListenAddress}:{_settings.Endpoint.ListenPort} cameras={cameraCount} nats={_settings.Endpoint.NatsUrl}").ConfigureAwait(false);
    }

    /// <summary>표준 입력을 한 줄씩 읽어 명령을 처리한다. EOF 또는 <c>quit</c>이면 반환한다.</summary>
    public async Task RunConsoleAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? line = await _input.ReadLineAsync(token).ConfigureAwait(false);
            if (line == null) return;
            if (HandleCommand(line)) return;
        }
    }

    /// <summary>명령 한 줄을 처리한다. 반환값 true는 종료(<c>quit</c>) 요청, 잘못된 명령은 usage 출력 후 계속.</summary>
    public bool HandleCommand(string command)
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        if (parts[0].Equals("quit", StringComparison.OrdinalIgnoreCase)) return true;
        if (parts[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine($"plc={_state.PlcOnline} cameras={_settings.Cameras.Count}");
            return false;
        }

        if (parts is ["plc", "online"])
        {
            _plc?.Start();
            _state.SetPlcOnline(true);
            return false;
        }
        if (parts is ["plc", "offline"])
        {
            _plc?.Stop();
            _state.SetPlcOnline(false);
            return false;
        }
        if (parts.Length == 4 && parts[0] == "plc" && parts[1] == "fault" && int.TryParse(parts[2], out int fault))
        {
            if (fault is < 0 or >= SimulatorState.FaultCount) { Usage(); return false; }
            if (parts[3] == "on") _state.SetFault(fault);
            else if (parts[3] == "off") _state.ClearFault(fault);
            else Usage();
            return false;
        }
        if (parts.Length == 3 && parts[0] == "camera" && Enum.TryParse(parts[2], ignoreCase: true, out CameraMode mode))
        {
            try { _state.SetCameraMode(parts[1], mode); }
            catch (ArgumentException) { Usage(); }
            return false;
        }

        Usage();
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _plc?.Dispose();
        if (_cameras != null) await _cameras.DisposeAsync().ConfigureAwait(false);
    }

    private void Usage() => _output.WriteLine("usage: status | plc online|offline | plc fault <0-19> on|off | camera <AgentId> online|fault|offline | quit");
}
