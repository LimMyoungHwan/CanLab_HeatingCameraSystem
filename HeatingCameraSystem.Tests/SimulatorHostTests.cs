using HeatingCameraSystem.Simulator;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Tests;

public class SimulatorHostTests
{
    [Fact]
    public async Task Start_PrintsReadiness_AndRunConsoleHandlesStatusQuit()
    {
        var output = new StringWriter();
        var input = new StringReader("status" + Environment.NewLine + "quit" + Environment.NewLine);
        var plc = new FakePlcEndpoint();
        var cameras = new FakeCameraEndpoint();
        var settings = Settings();
        await using var host = new SimulatorHost(settings, input: input, output: output, plcFactory: (_, _) => plc, cameraFactory: (_, _) => cameras);

        await host.StartAsync();
        await host.RunConsoleAsync(CancellationToken.None);

        string text = output.ToString();
        Assert.Contains("SIMULATOR READY plc=127.0.0.1:2004 cameras=2 nats=nats://127.0.0.1:4222", text);
        Assert.Contains("plc=True cameras=2", text);
        Assert.True(plc.Started);
        Assert.True(cameras.Started);
    }

    [Fact]
    public async Task Commands_UpdateState_AndInvalidCommandPrintsUsage()
    {
        var output = new StringWriter();
        var state = new SimulatorState(new[] { "Agent_0" });
        var plc = new FakePlcEndpoint();
        await using var host = new SimulatorHost(Settings(), state, output: output, plcFactory: (_, _) => plc, cameraFactory: (_, _) => new FakeCameraEndpoint());

        await host.StartAsync();
        host.HandleCommand("plc online");
        Assert.True(plc.Started);
        Assert.True(state.PlcOnline);
        host.HandleCommand("plc fault 3 on");
        Assert.True(state.GetFault(3));
        host.HandleCommand("plc fault 3 off");
        Assert.False(state.GetFault(3));
        host.HandleCommand("camera Agent_0 offline");
        Assert.Equal(CameraMode.Offline, state.GetCameraMode("Agent_0"));
        Assert.False(host.HandleCommand("bogus"));
        Assert.Contains("usage:", output.ToString());
        Assert.True(host.HandleCommand("quit"));
    }

    [Fact]
    public async Task Dispose_IsIdempotent_ForStartedResources()
    {
        var plc = new FakePlcEndpoint();
        var cameras = new FakeCameraEndpoint();
        var host = new SimulatorHost(Settings(), plcFactory: (_, _) => plc, cameraFactory: (_, _) => cameras);

        await host.StartAsync();
        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.True(plc.Disposed);
        Assert.True(cameras.Disposed);
    }

    private static SimulatorSettings Settings() => SimulatorSettings.CreateDefaults() with
    {
        OutputPath = Path.Combine(Path.GetTempPath(), "hcs_host_" + Guid.NewGuid().ToString("N"))
    };

    private sealed class FakePlcEndpoint : IPlcSimulatorEndpoint
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeCameraEndpoint : ICameraAgentEndpoint
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public Task StartAsync() { Started = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
