using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Plc;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Simulator;

public interface IPlcSimulatorEndpoint : IDisposable
{
    void Start();
    void Stop();
}

public interface ICameraAgentEndpoint : IAsyncDisposable
{
    Task StartAsync();
}

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

    public async Task RunConsoleAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? line = await _input.ReadLineAsync(token).ConfigureAwait(false);
            if (line == null) return;
            if (HandleCommand(line)) return;
        }
    }

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
