using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;

namespace HeatingCameraSystem.Tests;

public class DashboardLiveTrendTests
{
    [Fact]
    public async Task StatusAndLiveMessages_UpdateTile_OnlineCount_AndStaleState()
    {
        var nats = new FakeNats();
        var vm = new DashboardViewModel(new FakePlc(), nats, () => Array.Empty<Recipe>(), false);
        DateTime timestamp = DateTime.UtcNow;

        nats.AgentStatus(new AgentStatusMessage
        {
            AgentId = "Agent_0",
            CameraIndex = 0,
            CameraStatus = CameraStatus.Connected,
            Timestamp = timestamp
        });

        byte[] jpeg = CreateJpeg();
        nats.LiveFrame(new LiveFrameMessage
        {
            AgentId = "Agent_0",
            CameraIndex = 0,
            ImageBytes = jpeg,
            Width = 64,
            Height = 48,
            Timestamp = timestamp
        });

        CameraNode camera = await WaitForCameraAsync(vm, c => c.LiveImage != null);

        Assert.Equal(1, vm.OnlineAgentCount);
        Assert.Single(vm.CameraFeeds);
        Assert.Same(camera, vm.CameraFeeds[0].Camera);
        Assert.True(camera.HasFreshLiveFrame);
        Assert.Equal(timestamp, camera.LastLiveFrameUtc);

        vm.RefreshLiveFrameFreshness(timestamp.AddSeconds(3));
        Assert.False(camera.HasFreshLiveFrame);
    }

    [Fact]
    public async Task MalformedJpeg_DoesNotCrash_OrMutateLiveImage()
    {
        var nats = new FakeNats();
        var vm = new DashboardViewModel(new FakePlc(), nats, () => Array.Empty<Recipe>(), false);
        DateTime timestamp = DateTime.UtcNow;

        nats.LiveFrame(new LiveFrameMessage
        {
            AgentId = "Agent_0",
            CameraIndex = 0,
            ImageBytes = CreateJpeg(),
            Width = 64,
            Height = 48,
            Timestamp = timestamp
        });
        CameraNode camera = await WaitForCameraAsync(vm, c => c.LiveImage != null);
        var original = camera.LiveImage;

        nats.LiveFrame(new LiveFrameMessage
        {
            AgentId = "Agent_0",
            CameraIndex = 0,
            ImageBytes = new byte[] { 1, 2, 3 },
            Width = 64,
            Height = 48,
            Timestamp = timestamp.AddSeconds(1)
        });
        await Task.Delay(100);

        Assert.Same(original, camera.LiveImage);
        Assert.Equal(timestamp, camera.LastLiveFrameUtc);
    }

    [Fact]
    public async Task PlcSamples_AreBoundedAndNormalized()
    {
        var plc = new FakePlc();
        var vm = new DashboardViewModel(plc, new FakeNats(), () => Array.Empty<Recipe>(), false);

        for (int i = 0; i < 61; i++)
            await vm.RefreshPlcSnapshotAsync();

        Assert.Equal(60, vm.TemperatureTrendPoints.Count);
        Assert.Equal(60, vm.HumidityTrendPoints.Count);
        Assert.All(vm.TemperatureTrendPoints, p =>
        {
            Assert.InRange(p.X, 0, 100);
            Assert.InRange(p.Y, 0, 40);
        });
        Assert.All(vm.HumidityTrendPoints, p =>
        {
            Assert.InRange(p.X, 0, 100);
            Assert.InRange(p.Y, 0, 40);
        });
    }

    [Fact]
    public void MainViewModel_ReusesDashboardInstance()
    {
        var vm = new MainViewModel();
        object? first = vm.CurrentViewModel;

        vm.NavigateToLiveViewCommand.Execute(null);
        vm.NavigateToDashboardCommand.Execute(null);
        object? second = vm.CurrentViewModel;
        vm.NavigateToLiveViewCommand.Execute(null);
        vm.NavigateToDashboardCommand.Execute(null);

        Assert.Same(first, second);
        Assert.Same(first, vm.CurrentViewModel);
    }

    private static byte[] CreateJpeg()
    {
        var scene = new SyntheticThermalScene(new FrameSettings(64, 48), () => 1);
        return ThermalPreviewEncoder.EncodeColorJpeg(scene.NextFrame(0));
    }

    private static async Task<CameraNode> WaitForCameraAsync(DashboardViewModel vm, Func<CameraNode, bool> predicate)
    {
        for (int i = 0; i < 50; i++)
        {
            CameraNode? camera = vm.Agents.SelectMany(a => a.Cameras).FirstOrDefault(predicate);
            if (camera != null) return camera;
            await Task.Delay(20);
        }

        throw new TimeoutException("Camera update was not observed.");
    }

    private sealed class FakeNats : INatsCommunicationService
    {
        private Action<AgentStatusMessage>? _status;
        private Action<LiveFrameMessage>? _live;

        public void AgentStatus(AgentStatusMessage message) => _status?.Invoke(message);
        public void LiveFrame(LiveFrameMessage message) => _live?.Invoke(message);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task ConnectAsync(string natsUrl = "nats://127.0.0.1:4222") => Task.CompletedTask;
        public Task PublishCaptureCommandAsync(CaptureCommandMessage message) => Task.CompletedTask;
        public Task SubscribeCaptureCommandAsync(string agentId, Action<CaptureCommandMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishAgentStatusAsync(AgentStatusMessage message) => Task.CompletedTask;
        public Task SubscribeAgentStatusAsync(Action<AgentStatusMessage> onMessageReceived) { _status = onMessageReceived; return Task.CompletedTask; }
        public Task PublishCaptureResultAsync(CaptureResultMessage message) => Task.CompletedTask;
        public Task SubscribeCaptureResultAsync(Action<CaptureResultMessage> onMessageReceived) => Task.CompletedTask;
        public Task PublishLiveFrameAsync(LiveFrameMessage message) => Task.CompletedTask;
        public Task SubscribeLiveFrameAsync(Action<LiveFrameMessage> onMessageReceived) { _live = onMessageReceived; return Task.CompletedTask; }
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

    private sealed class FakePlc : IPlcController
    {
        private int _sample;
        public bool IsConnected => true;
        public Task ConnectAsync(string ipAddress, int port = 2004) => Task.CompletedTask;
        public void Disconnect() { }
        public Task StartChamberAsync() => Task.CompletedTask;
        public Task StopChamberAsync() => Task.CompletedTask;
        public Task SetTargetTemperatureAsync(float temperature) => Task.CompletedTask;
        public Task<float> GetCurrentTemperatureAsync() => Task.FromResult((float)(_sample++ % 101));
        public Task SetTargetHumidityAsync(float humidity) => Task.CompletedTask;
        public Task<float> GetCurrentHumidityAsync() => Task.FromResult((float)(_sample % 101));
        public Task SetHumidityControlAsync(bool on) => Task.CompletedTask;
        public Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature) => Task.CompletedTask;
        public Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex) => Task.FromResult(0f);
        public Task MoveServoToPositionAsync(int positionIndex) => Task.CompletedTask;
        public Task<bool> IsServoAtPositionAsync(int positionIndex) => Task.FromResult(true);
        public Task SetServoSpeedAsync(int percent) => Task.CompletedTask;
        public Task JogAsync(ServoAxis axis, bool positive, bool on) => Task.CompletedTask;
        public Task HomeAsync(ServoAxis axis) => Task.CompletedTask;
        public Task SetPointCoordinateAsync(int positionIndex, int x, int y) => Task.CompletedTask;
        public Task<(int X, int Y)> GetPointCoordinateAsync(int positionIndex) => Task.FromResult((0, 0));
        public Task MoveToCoordinateAsync(int x, int y) => Task.CompletedTask;
        public Task SetEquipmentAsync(PlcEquipment equipment, bool on) => Task.CompletedTask;
        public Task SetFanSpeedAsync(float hz) => Task.CompletedTask;
        public Task WriteAdminSettingsAsync(PlcAdminSettings settings) => Task.CompletedTask;
        public Task<PlcStatusSnapshot> ReadStatusAsync() => Task.FromResult(new PlcStatusSnapshot());
        public Task TriggerEmergencyStopAsync() => Task.CompletedTask;
    }
}
