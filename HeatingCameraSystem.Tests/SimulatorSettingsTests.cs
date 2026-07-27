using System.Text.Json;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Locks the simulator configuration contract (defaults, JSON round-trip, every rejection
/// path) and the thread-safety of <see cref="SimulatorState"/>. All scratch I/O uses
/// <see cref="Path.GetTempPath"/> — never %LOCALAPPDATA%.
/// </summary>
public class SimulatorSettingsTests
{
    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), "HCS_SimSettings_" + Guid.NewGuid().ToString("N"));

    private static SimulatorSettings ValidWith(string outputPath) => new(
        new EndpointSettings(),
        new DynamicsSettings(),
        new FrameSettings(),
        outputPath,
        new[] { new CameraSettings("Agent_0", 0), new CameraSettings("Agent_1", 1) });

    private static void AssertRejects(SimulatorSettings settings, string propertyName)
    {
        var ex = Assert.Throws<SimulatorSettingsException>(settings.Validate);
        Assert.Contains(propertyName, ex.Message, StringComparison.Ordinal);
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // (a) defaults
    [Fact]
    public void Defaults_MatchSpecification()
    {
        var s = SimulatorSettings.CreateDefaults();

        Assert.Equal("127.0.0.1", s.Endpoint.ListenAddress);
        Assert.Equal(2004, s.Endpoint.ListenPort);
        Assert.Equal("nats://127.0.0.1:4222", s.Endpoint.NatsUrl);
        Assert.Equal(100, s.Dynamics.TickMs);
        Assert.Equal(20.0, s.Dynamics.TemperatureRatePerSecond);
        Assert.Equal(40.0, s.Dynamics.HumidityRatePerSecond);
        Assert.Equal(30.0, s.Dynamics.BlackbodyRatePerSecond);
        Assert.Equal(500, s.Dynamics.ServoBusyMs);
        Assert.Equal(5, s.Dynamics.HeartbeatSeconds);
        Assert.Equal(100, s.Dynamics.LiveFrameIntervalMs);
        Assert.Equal(640, s.Frame.Width);
        Assert.Equal(480, s.Frame.Height);
        Assert.StartsWith(AppContext.BaseDirectory, s.OutputPath);
        Assert.EndsWith("ImageStorage", s.OutputPath);
        Assert.Equal(2, s.Cameras.Count);
        Assert.Equal("Agent_0", s.Cameras[0].AgentId);
        Assert.Equal(0, s.Cameras[0].CameraIndex);
        Assert.Equal("Agent_1", s.Cameras[1].AgentId);
        Assert.Equal(1, s.Cameras[1].CameraIndex);
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaultsAndPersists()
    {
        string dir = NewTempPath();
        string path = Path.Combine(dir, "simulator.json");
        try
        {
            var s = SimulatorSettings.Load(path);

            Assert.True(File.Exists(path));
            Assert.Equal(2, s.Cameras.Count);
            Assert.Equal(2004, s.Endpoint.ListenPort);
        }
        finally { Cleanup(dir); }
    }

    // (b) serialize -> deserialize round-trip
    [Fact]
    public void JsonRoundTrip_PreservesAllComponents()
    {
        var original = ValidWith(NewTempPath()) with
        {
            Endpoint = new EndpointSettings("127.0.0.1", 2010, "nats://127.0.0.1:5222"),
            Dynamics = new DynamicsSettings(50, 11.0, 22.0, 33.0, 250, 3, 40),
            Frame = new FrameSettings(320, 240),
            Cameras = new[] { new CameraSettings("Agent_A", 0), new CameraSettings("Agent_B", 7) }
        };

        string json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<SimulatorSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Endpoint, restored!.Endpoint);
        Assert.Equal(original.Dynamics, restored.Dynamics);
        Assert.Equal(original.Frame, restored.Frame);
        Assert.Equal(original.OutputPath, restored.OutputPath);
        Assert.Equal(original.Cameras, restored.Cameras);
    }

    // (c) duplicate AgentId
    [Fact]
    public void Validate_DuplicateAgentId_Rejected() =>
        AssertRejects(
            ValidWith(NewTempPath()) with
            {
                Cameras = new[] { new CameraSettings("Dup", 0), new CameraSettings("Dup", 1) }
            },
            "AgentId");

    // (d) duplicate camera index
    [Fact]
    public void Validate_DuplicateCameraIndex_Rejected() =>
        AssertRejects(
            ValidWith(NewTempPath()) with
            {
                Cameras = new[] { new CameraSettings("Agent_0", 3), new CameraSettings("Agent_1", 3) }
            },
            "CameraIndex");

    // (e) invalid (non-loopback / unparseable) listen address
    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("0.0.0.0")]
    [InlineData("not-an-ip")]
    public void Validate_NonLoopbackListenAddress_Rejected(string addr) =>
        AssertRejects(
            ValidWith(NewTempPath()) with
            {
                Endpoint = new EndpointSettings(addr, 2004, "nats://127.0.0.1:4222")
            },
            "ListenAddress");

    // (f) port out of range
    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_PortOutOfRange_Rejected(int port) =>
        AssertRejects(
            ValidWith(NewTempPath()) with
            {
                Endpoint = new EndpointSettings("127.0.0.1", port, "nats://127.0.0.1:4222")
            },
            "ListenPort");

    // (g) zero/negative rate and interval
    [Fact]
    public void Validate_ZeroTick_Rejected() =>
        AssertRejects(
            ValidWith(NewTempPath()) with { Dynamics = new DynamicsSettings() with { TickMs = 0 } },
            "TickMs");

    [Fact]
    public void Validate_NegativeTemperatureRate_Rejected() =>
        AssertRejects(
            ValidWith(NewTempPath()) with { Dynamics = new DynamicsSettings() with { TemperatureRatePerSecond = -1.0 } },
            "TemperatureRatePerSecond");

    [Fact]
    public void Validate_ZeroLiveFrameInterval_Rejected() =>
        AssertRejects(
            ValidWith(NewTempPath()) with { Dynamics = new DynamicsSettings() with { LiveFrameIntervalMs = 0 } },
            "LiveFrameIntervalMs");

    // (h) odd frame dimension
    [Theory]
    [InlineData(641, 480, "Width")]
    [InlineData(640, 481, "Height")]
    public void Validate_OddFrameDimension_Rejected(int w, int h, string prop) =>
        AssertRejects(ValidWith(NewTempPath()) with { Frame = new FrameSettings(w, h) }, prop);

    // (i) 0 cameras and 65 cameras
    [Fact]
    public void Validate_ZeroCameras_Rejected() =>
        AssertRejects(ValidWith(NewTempPath()) with { Cameras = Array.Empty<CameraSettings>() }, "Cameras");

    [Fact]
    public void Validate_TooManyCameras_Rejected()
    {
        var cams = Enumerable.Range(0, 65).Select(i => new CameraSettings($"Agent_{i}", i)).ToArray();
        AssertRejects(ValidWith(NewTempPath()) with { Cameras = cams }, "Cameras");
    }

    // (j) unwritable output path (a file where a directory is expected)
    [Fact]
    public void Validate_UnwritableOutputPath_Rejected()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "HCS_SimBlock_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(filePath, "block");
        try
        {
            AssertRejects(ValidWith(filePath), "OutputPath");
        }
        finally { File.Delete(filePath); }
    }

    // QA Failure (unit level): malformed JSON -> actionable, typed, named-property exception.
    [Fact]
    public void Load_MalformedJson_ThrowsActionableExceptionNamingProperty()
    {
        string dir = NewTempPath();
        string path = Path.Combine(dir, "bad.json");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(path,
                "{ \"Endpoint\": { \"ListenAddress\": \"127.0.0.1\", \"ListenPort\": \"NOT_A_PORT\", \"NatsUrl\": \"nats://127.0.0.1:4222\" } }");

            var ex = Assert.Throws<SimulatorSettingsException>(() => SimulatorSettings.Load(path));

            Assert.Contains("ListenPort", ex.Message, StringComparison.Ordinal);
            Assert.Contains("invalid JSON", ex.Message, StringComparison.Ordinal);
        }
        finally { Cleanup(dir); }
    }

    // (k) concurrent SimulatorState transitions converge to a consistent final state
    [Fact]
    public async Task SimulatorState_ConcurrentTransitions_ConvergeConsistently()
    {
        string[] agents = { "Agent_0", "Agent_1", "Agent_2", "Agent_3" };
        var state = new SimulatorState(agents);
        int workers = Math.Max(4, Environment.ProcessorCount * 4);
        const int iters = 5000;

        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < iters; i++)
            {
                state.SetPlcOnline((i & 1) == 0);
                int fi = i % SimulatorState.FaultCount;
                if ((i & 1) == 0) state.SetFault(fi); else state.ClearFault(fi);
                state.SetCameraMode(agents[i % agents.Length], (CameraMode)(i % 3));
                _ = state.PlcOnline;
                _ = state.SnapshotFaults();
            }
        })).ToArray();

        await Task.WhenAll(tasks); // torn Dictionary/array under a missing lock throws here

        // Deterministic final writes must stick, proving the lock is still consistent post-contention.
        state.SetPlcOnline(true);
        for (int i = 0; i < SimulatorState.FaultCount; i++) state.SetFault(i);
        foreach (string a in agents) state.SetCameraMode(a, CameraMode.Offline);

        Assert.True(state.PlcOnline);
        Assert.All(state.SnapshotFaults(), b => Assert.True(b));
        Assert.All(agents, a => Assert.Equal(CameraMode.Offline, state.GetCameraMode(a)));
    }

    [Fact]
    public void SimulatorState_UnknownAgentAndBadFaultIndex_Throw()
    {
        var state = new SimulatorState(new[] { "Agent_0" });

        Assert.Throws<ArgumentException>(() => state.SetCameraMode("Nope", CameraMode.Faulted));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetFault(SimulatorState.FaultCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetFault(-1));
    }
}
