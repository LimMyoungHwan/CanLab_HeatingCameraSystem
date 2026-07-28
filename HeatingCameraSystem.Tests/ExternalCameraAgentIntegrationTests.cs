using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.State;

namespace HeatingCameraSystem.Tests;

[Trait("Category", "ExternalNats")]
public class ExternalCameraAgentIntegrationTests
{
    [Fact]
    public async Task Simulator_PublishesStatusLiveAndSuccessfulCapture_WithExactIdentity()
    {
        using var scope = new TempOutput();
        var settings = Settings(scope.Path);
        var state = new SimulatorState(settings.Cameras.Select(c => c.AgentId));
        var nats = new FakeNats();
        await using var simulator = new NatsCameraAgentSimulator(settings, state, nats);

        await simulator.StartAsync();
        await WaitUntilAsync(() => nats.Statuses.Count >= 2, TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => nats.LiveFrames.Count >= 2, TimeSpan.FromSeconds(2));

        await nats.SendCaptureAsync("Agent_0", "step-a");

        CaptureResultMessage result = await WaitForResultAsync(nats, "Agent_0", TimeSpan.FromSeconds(2));
        Assert.True(result.IsSuccess);
        Assert.Equal("Agent_0", result.AgentId);
        Assert.Equal("step-a", result.RecipeStepId);
        Assert.NotNull(result.ImageBytes);
        Assert.True(result.ImageBytes!.Length > 0);
        Assert.True(File.Exists(result.ImagePath));
        Assert.Contains(nats.LiveFrames, f => f.AgentId == "Agent_0" && f.CameraIndex == 0 && f.ImageBytes?.Length > 3);
        Assert.Contains(nats.LiveFrames, f => f.AgentId == "Agent_1" && f.CameraIndex == 1 && f.ImageBytes?.Length > 3);
    }

    [Fact]
    public async Task BroadcastCapture_FansOutOncePerCamera_WithUniqueFiles()
    {
        using var scope = new TempOutput();
        var settings = Settings(scope.Path);
        var nats = new FakeNats();
        await using var simulator = new NatsCameraAgentSimulator(settings, null, nats);

        await simulator.StartAsync();
        await nats.SendCaptureAsync("all", "broadcast-1");
        await WaitUntilAsync(() => nats.Results.Count >= 2, TimeSpan.FromSeconds(2));

        Assert.Equal(2, nats.Results.Count(r => r.RecipeStepId == "broadcast-1"));
        Assert.Equal(2, nats.Results.Select(r => r.ImagePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task FaultedFailsCapture_OfflineIsSilent_AndOtherCameraContinues()
    {
        using var scope = new TempOutput();
        var settings = Settings(scope.Path);
        var state = new SimulatorState(settings.Cameras.Select(c => c.AgentId));
        var nats = new FakeNats();
        await using var simulator = new NatsCameraAgentSimulator(settings, state, nats);

        await simulator.StartAsync();
        state.SetCameraMode("Agent_1", CameraMode.Faulted);
        await nats.SendCaptureAsync("Agent_1", "fault");
        CaptureResultMessage failed = await WaitForResultAsync(nats, "Agent_1", TimeSpan.FromSeconds(2));
        Assert.False(failed.IsSuccess);
        Assert.Null(failed.ImageBytes);

        state.SetCameraMode("Agent_1", CameraMode.Offline);
        int resultCount = nats.Results.Count;
        int statusCount = nats.Statuses.Count(s => s.AgentId == "Agent_1");
        await nats.SendCaptureAsync("Agent_1", "offline");
        await Task.Delay(300);
        Assert.Equal(resultCount, nats.Results.Count);
        Assert.Equal(statusCount, nats.Statuses.Count(s => s.AgentId == "Agent_1"));

        await nats.SendCaptureAsync("Agent_0", "still-online");
        CaptureResultMessage ok = await WaitForResultAsync(nats, "Agent_0", TimeSpan.FromSeconds(2));
        Assert.True(ok.IsSuccess);
    }

    private static SimulatorSettings Settings(string outputPath) => SimulatorSettings.CreateDefaults() with
    {
        OutputPath = outputPath,
        Dynamics = new DynamicsSettings(100, 20, 40, 30, 500, 1, 50),
        Frame = new FrameSettings(64, 48)
    };

    private static async Task<CaptureResultMessage> WaitForResultAsync(FakeNats nats, string agentId, TimeSpan timeout)
    {
        await WaitUntilAsync(() => nats.Results.Any(r => r.AgentId == agentId), timeout);
        return nats.Results.Last(r => r.AgentId == agentId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition was not met before the deadline.");
    }

    private sealed class TempOutput : IDisposable
    {
        public TempOutput() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hcs_nats_sim_" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class FakeNats : INatsCommunicationService
    {
        private readonly Dictionary<string, Action<CaptureCommandMessage>> _captures = new(StringComparer.Ordinal);
        private readonly List<Action<CaptureCommandMessage>> _broadcasts = new();
        public List<AgentStatusMessage> Statuses { get; } = new();
        public List<LiveFrameMessage> LiveFrames { get; } = new();
        public List<CaptureResultMessage> Results { get; } = new();

        public async Task SendCaptureAsync(string agentId, string stepId)
        {
            var message = new CaptureCommandMessage { TargetAgentId = agentId, RecipeStepId = stepId, Timestamp = DateTime.UtcNow };
            if (agentId == "all")
            {
                foreach (Action<CaptureCommandMessage> callback in _broadcasts.ToArray()) callback(message);
            }
            else if (_captures.TryGetValue(agentId, out Action<CaptureCommandMessage>? callback))
            {
                callback(message);
            }
            await Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222") => Task.CompletedTask;
        public Task PublishCaptureCommandAsync(CaptureCommandMessage message) => SendCaptureAsync(message.TargetAgentId, message.RecipeStepId);
        public Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived)
        {
            _captures[agentId] = onMessageReceived;
            _broadcasts.Add(onMessageReceived);
            return Task.CompletedTask;
        }
        public Task PublishAgentStatusAsync(AgentStatusMessage message) { Statuses.Add(message); return Task.CompletedTask; }
        public Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishCaptureResultAsync(CaptureResultMessage message) { Results.Add(message); return Task.CompletedTask; }
        public Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishLiveFrameAsync(LiveFrameMessage message) { LiveFrames.Add(message); return Task.CompletedTask; }
        public Task SubscribeLiveFrameAsync(Action<LiveFrameMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishSerialConfigAsync(SerialConfigMessage message) => Task.CompletedTask;
        public Task SubscribeSerialConfigAsync(string agentId, Action<SerialConfigMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishSerialConfigAckAsync(SerialConfigAckMessage message) => Task.CompletedTask;
        public Task SubscribeSerialConfigAckAsync(string agentId, Action<SerialConfigAckMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishAgentConfigRequestAsync(AgentConfigRequestMessage message) => Task.CompletedTask;
        public Task SubscribeAgentConfigRequestAsync(string agentId, Action<AgentConfigRequestMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishAgentConfigSnapshotAsync(AgentConfigSnapshotMessage message) => Task.CompletedTask;
        public Task SubscribeAgentConfigSnapshotAsync(string agentId, Action<AgentConfigSnapshotMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishAgentConfigApplyAsync(AgentConfigApplyMessage message) => Task.CompletedTask;
        public Task SubscribeAgentConfigApplyAsync(string agentId, Action<AgentConfigApplyMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishAgentConfigAckAsync(AgentConfigAckMessage message) => Task.CompletedTask;
        public Task SubscribeAgentConfigAckAsync(string agentId, Action<AgentConfigAckMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishCameraInventoryAsync(CameraInventoryMessage message) => Task.CompletedTask;
        public Task SubscribeCameraInventoryAsync(Action<CameraInventoryMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishManagerCommandAsync(ManagerCommandMessage message) => Task.CompletedTask;
        public Task SubscribeManagerCommandAsync(string pcId, Action<ManagerCommandMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishLogAlertAsync(LogAlertMessage message) => Task.CompletedTask;
        public Task SubscribeLogAlertAsync(Action<LogAlertMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishLogDumpRequestAsync(LogDumpRequestMessage message) => Task.CompletedTask;
        public Task SubscribeLogDumpRequestAsync(string pcId, Action<LogDumpRequestMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishLogDumpAsync(LogDumpMessage message) => Task.CompletedTask;
        public Task SubscribeLogDumpAsync(string pcId, Action<LogDumpMessage> onMessageReceived) => Task.CompletedTask;
    }
}
