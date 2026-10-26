using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Moq;

namespace HeatingCameraSystem.Tests;

public class ConnectorDeepAuditTests
{
    [Fact]
    public async Task ReconnectedCamera_UsesCurrentDescriptorInsteadOfSubscriptionTimeIndex()
    {
        using var f = new Fixture();
        CameraDescriptor original = f.Camera;
        CameraDescriptor updated = original with { OpenCvIndex = 3, SerialPortName = "COM9" };
        CameraDescriptor? handled = null;
        await using var connector = f.Create(control: (camera, _) =>
        {
            handled = camera;
            return Task.FromResult((true, "ok"));
        });
        f.Cameras[0] = updated;
        await connector.HandleCameraControlAsync(original, new CameraControlMessage
        {
            AgentId = original.AgentId, CameraIndex = 3, Op = CameraControlOps.Run, RequestId = "current"
        });
        Assert.Same(updated, handled);
        CameraControlAckMessage ack = Assert.Single(f.Acks);
        Assert.True(ack.IsSuccess);
        Assert.Equal(3, ack.CameraIndex);
    }

    [Fact]
    public async Task OldCameraIndex_IsRejectedBeforeHardwareHandler()
    {
        using var f = new Fixture();
        CameraDescriptor original = f.Camera;
        int calls = 0;
        await using var connector = f.Create(control: (_, _) =>
        {
            calls++;
            return Task.FromResult((true, "ok"));
        });
        CameraControlMessage oldCommand = f.Control(CameraControlOps.Run);
        f.Cameras[0] = original with { OpenCvIndex = 3 };
        await connector.HandleCameraControlAsync(original, oldCommand);
        Assert.Equal(0, calls);
        CameraControlAckMessage ack = Assert.Single(f.Acks);
        Assert.False(ack.IsSuccess);
        Assert.Equal(3, ack.CameraIndex);
        Assert.Contains("인덱스", ack.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericHardwareFailure_IsReportedAsFailedAck(bool throws)
    {
        using var f = new Fixture();
        await using var connector = f.Create(control: (_, _) => throws
            ? Task.FromException<(bool, string)>(new IOException("serial disconnected"))
            : Task.FromResult((false, "camera rejected command")));
        await connector.HandleCameraControlAsync(f.Camera, f.Control(CameraControlOps.ShutterClose));
        CameraControlAckMessage ack = Assert.Single(f.Acks);
        Assert.False(ack.IsSuccess);
        Assert.Contains(throws ? "serial disconnected" : "camera rejected", ack.Message);
    }

    [Fact]
    public async Task HardwareChangeDuringBias_IsRejectedWithoutInterleavingSerialCommands()
    {
        using var f = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int genericCalls = 0;
        await using var connector = f.Create(
            control: (_, _) => { genericCalls++; return Task.FromResult((true, "ok")); },
            bias: async (_, _, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); });
        var bias = f.Control(CameraControlOps.BiasLow);
        bias.RequestId = "bias-one";
        Task running = connector.HandleCameraControlAsync(f.Camera, bias);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await connector.HandleCameraControlAsync(f.Camera, f.Control(CameraControlOps.SaveConfig));
        Assert.Equal(0, genericCalls);
        Assert.False(Assert.Single(f.Acks).IsSuccess);
        await connector.HandleCameraControlAsync(f.Camera, new CameraControlMessage
        {
            AgentId = f.Camera.AgentId, CameraIndex = 0, Op = CameraControlOps.BiasAbort,
            RequestId = "abort", TargetRequestId = "bias-one"
        });
        await running.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StaleProductionFrame_FailsWithoutSavingOldFrameAsNewShot()
    {
        using var f = new Fixture();
        ThermalFrame stale = Frame(DateTimeOffset.UtcNow.AddMinutes(-1));
        f.Runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);
        await using var connector = f.Create();
        await connector.HandleCaptureAsync(f.Camera, f.Capture());
        CaptureResultMessage result = Assert.Single(f.Results);
        Assert.False(result.IsSuccess);
        Assert.Contains("이전 프레임", result.Message);
        Assert.Empty(f.Store.Query());
        Assert.Empty(Directory.GetFiles(f.Root, "*.raw", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RuntimeReplacedWhileAwaitingFrame_DoesNotSaveFrameFromOldCamera()
    {
        using var f = new Fixture();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = new TaskCompletionSource<ThermalFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback(() => waiting.TrySetResult()).Returns(next.Task);
        await using var connector = f.Create();
        Task capture = connector.HandleCaptureAsync(f.Camera, f.Capture());
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        f.ReplacementRuntime = new Mock<ICameraRuntime>().Object;
        f.Manager.Add(f.Camera);
        next.SetResult(Frame(DateTimeOffset.UtcNow));
        await capture;
        Assert.False(Assert.Single(f.Results).IsSuccess);
        Assert.Empty(f.Store.Query());
    }

    [Fact]
    public async Task SameTimestampInBurst_IsNotCountedTwice()
    {
        using var f = new Fixture();
        ThermalFrame repeated = Frame(DateTimeOffset.UtcNow.AddSeconds(2));
        f.Runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(repeated);
        await using var connector = f.Create();
        CaptureCommandMessage command = f.Capture();
        command.ShotCount = 2;
        await connector.HandleCaptureAsync(f.Camera, command);
        CaptureResultMessage result = Assert.Single(f.Results);
        Assert.False(result.IsSuccess);
        Assert.Contains("촬영 1/2", result.Message);
        string finalFolder = Path.Combine(command.StorageRootUnc, "S1_P1", "LNN30", "hot");
        Assert.Single(Directory.GetFiles(finalFolder, "*.raw"));
    }

    [Fact]
    public async Task ConfigResponses_EchoRequestIdAndReportSavedDraft()
    {
        using var f = new Fixture();
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<AgentConfigRequestMessage>? requestCallback = null;
        Action<AgentConfigApplyMessage>? applyCallback = null;
        f.Nats.Setup(n => n.SubscribeAgentConfigRequestAsync(f.Camera.AgentId, It.IsAny<Action<AgentConfigRequestMessage>>()))
            .Callback<string, Action<AgentConfigRequestMessage>>((_, callback) => requestCallback = callback).Returns(Task.CompletedTask);
        f.Nats.Setup(n => n.SubscribeAgentConfigApplyAsync(f.Camera.AgentId, It.IsAny<Action<AgentConfigApplyMessage>>()))
            .Callback<string, Action<AgentConfigApplyMessage>>((_, callback) => { applyCallback = callback; subscribed.TrySetResult(); })
            .Returns(Task.CompletedTask);
        var snapshot = new TaskCompletionSource<AgentConfigSnapshotMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ack = new TaskCompletionSource<AgentConfigAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Nats.Setup(n => n.PublishAgentConfigSnapshotAsync(It.IsAny<AgentConfigSnapshotMessage>()))
            .Callback<AgentConfigSnapshotMessage>(msg => snapshot.TrySetResult(msg)).Returns(Task.CompletedTask);
        f.Nats.Setup(n => n.PublishAgentConfigAckAsync(It.IsAny<AgentConfigAckMessage>()))
            .Callback<AgentConfigAckMessage>(msg => ack.TrySetResult(msg)).Returns(Task.CompletedTask);
        AgentConfigSnapshot saved = new();
        await using var connector = new CameraNatsConnector(f.Nats.Object, f.Manager, f.Store, f.Cameras,
            getConfigSnapshot: () => saved,
            applyConfigSnapshot: config => saved = config);
        connector.Start("nats://test");
        await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(requestCallback);
        requestCallback(new AgentConfigRequestMessage { AgentId = f.Camera.AgentId, RequestId = "read-1" });
        Assert.Equal("read-1", (await snapshot.Task.WaitAsync(TimeSpan.FromSeconds(2))).RequestId);
        Assert.NotNull(applyCallback);
        applyCallback(new AgentConfigApplyMessage
        {
            AgentId = f.Camera.AgentId, RequestId = "apply-2", Config = new AgentConfigSnapshot { RestartRequired = true }
        });
        AgentConfigAckMessage result = await ack.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.IsSuccess);
        Assert.Equal("apply-2", result.RequestId);
        Assert.Contains("재시작", result.Message);
    }

    private static ThermalFrame Frame(DateTimeOffset timestamp)
        => new(Enumerable.Range(0, 64 * 64).Select(i => (ushort)(1000 + i % 4096)).ToArray(), 64, 64, timestamp);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hcs_connector_audit_" + Guid.NewGuid().ToString("N"));
        public CameraDescriptor Camera => Cameras[0];
        public List<CameraDescriptor> Cameras { get; } = new() { new("cam", 0, "Camera", "COM1", CameraSerialNumber: "S1", UsbContainerId: "container") };
        public Mock<INatsCommunicationService> Nats { get; } = new();
        public Mock<ICameraRuntime> Runtime { get; } = new();
        public ICameraRuntime? ReplacementRuntime { get; set; }
        public CameraRuntimeManager Manager { get; }
        public CaptureStore Store { get; }
        public ConcurrentQueue<CameraControlAckMessage> Acks { get; } = new();
        public ConcurrentQueue<CaptureResultMessage> Results { get; } = new();
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new CaptureStore(Path.Combine(Root, "captures"), new LiteDbCaptureIndex(Path.Combine(Root, "index.db")));
            Manager = new CameraRuntimeManager(_ => ReplacementRuntime ?? Runtime.Object);
            Manager.Add(Camera);
            Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                .Callback<CameraControlAckMessage>(Acks.Enqueue).Returns(Task.CompletedTask);
            Nats.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                .Callback<CaptureResultMessage>(Results.Enqueue).Returns(Task.CompletedTask);
        }
        public CameraNatsConnector Create(
            Func<CameraDescriptor, CameraControlMessage, Task<(bool Success, string Message)>>? control = null,
            Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task>? bias = null)
            => new(Nats.Object, Manager, Store, Cameras, cameraControlHandler: control, biasControlHandler: bias,
                productionSink: new ProductionCaptureSink(Path.Combine(Root, "buffer")));
        public CameraControlMessage Control(string op)
            => new() { AgentId = Camera.AgentId, CameraIndex = Camera.OpenCvIndex, Op = op, RequestId = "command" };
        public CaptureCommandMessage Capture() => new()
        {
            TargetAgentId = Camera.AgentId, ShotCount = 1, Source = CaptureSource.Manual,
            StorageRootUnc = Path.Combine(Root, "final"), ProductNumber = "P1", ConditionFolder = "LNN30",
            BlackBodyFolder = "hot", FilePrefix = "BB20"
        };
        public void Dispose()
        {
            Manager.Dispose();
            Store.Dispose();
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }
}
