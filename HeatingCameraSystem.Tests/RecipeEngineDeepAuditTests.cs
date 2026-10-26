using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using Moq;

namespace HeatingCameraSystem.Tests;

public sealed class RecipeEngineDeepAuditTests
{
    [Theory]
    [InlineData(CameraControlOps.ShutterOpen)]
    [InlineData(CameraControlOps.ShutterClose)]
    [InlineData(CameraControlOps.Run)]
    [InlineData(CameraControlOps.Nuc)]
    public async Task FailedControlAck_IsNgAndBlocksLaterCaptureAndMovement(string operation)
    {
        var f = new Fixture();
        f.Bus.Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
            .Callback<CameraControlMessage>(command => f.Bus.Emit($"agent.ack.camera.{command.AgentId}", new CameraControlAckMessage
            {
                AgentId = command.AgentId, CameraIndex = command.CameraIndex, Op = command.Op,
                RequestId = command.RequestId, IsSuccess = false, Message = "serial rejection"
            })).Returns(Task.CompletedTask);
        var recipe = new Recipe { Steps = new() { CameraStep(operation), CameraStep(CameraControlOps.Capture), MoveStep() } };
        RecipeNgException error = await Assert.ThrowsAsync<RecipeNgException>(() => f.Engine.ExecuteRecipeAsync(recipe));
        Assert.Equal(AlarmCodes.CameraCommandFailed, error.AlarmCode);
        Assert.Contains("serial rejection", error.Message);
        f.Bus.Nats.Verify(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
        f.Plc.Verify(service => service.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(f.TerminalEvents).Outcome);
        Assert.Equal(0, f.Bus.ActiveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureFromDifferentAgentOrCamera_IsNgAndDoesNotStoreOrMove(bool wrongAgent)
    {
        var f = new Fixture();
        f.Bus.Nats.Setup(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
            .Callback<CaptureCommandMessage>(command =>
            {
                CaptureResultMessage result = Result(command, "wrong");
                if (wrongAgent) result.AgentId = "B"; else result.CameraIndex++;
                f.Bus.Emit("agent.result.capture.A", result);
            }).Returns(Task.CompletedTask);
        RecipeNgException error = await Assert.ThrowsAsync<RecipeNgException>(() =>
            f.Engine.ExecuteRecipeAsync(new Recipe { Steps = new() { CameraStep(CameraControlOps.Capture), MoveStep() } }));
        Assert.Equal(AlarmCodes.CameraCommandFailed, error.AlarmCode);
        Assert.Empty(f.History);
        Assert.Contains(f.Controls, command => command.Op == CameraControlOps.CaptureAbort && !string.IsNullOrWhiteSpace(command.TargetRequestId));
        f.Plc.Verify(service => service.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
        Assert.Equal(0, f.Bus.ActiveCount);
    }

    [Fact]
    public async Task DuplicateCaptureId_DoesNotCountAsAnotherShot()
    {
        var f = new Fixture();
        f.Bus.Nats.Setup(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
            .Callback<CaptureCommandMessage>(command =>
            {
                f.Bus.Emit("agent.result.capture.A", Result(command, "one"));
                f.Bus.Emit("agent.result.capture.A", Result(command, "one"));
                f.Bus.Emit("agent.result.capture.A", Result(command, "two"));
            }).Returns(Task.CompletedTask);
        RecipeStep step = CameraStep(CameraControlOps.Capture); step.ShotCount = 2;
        await f.Engine.ExecuteRecipeAsync(new Recipe { Steps = new() { step } });
        Assert.Equal(new[] { "one.y16", "two.y16" }, f.History.Select(record => record.AgentRawPath));
        Assert.Equal(0, f.Bus.ActiveCount);
    }

    [Fact]
    public async Task LaterRun_RejectsOldResponseAndReleasesAllRunSubscriptions()
    {
        var f = new Fixture();
        var sent = new List<CaptureCommandMessage>();
        f.Bus.Nats.Setup(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
            .Callback<CaptureCommandMessage>(command => sent.Add(command)).Returns(Task.CompletedTask);
        Recipe recipe = new() { Steps = new() { CameraStep(CameraControlOps.Capture) } };
        Task first = f.Engine.ExecuteRecipeAsync(recipe);
        f.Bus.Emit("agent.result.capture.A", Result(sent[0], "first"));
        await first;
        Assert.Equal(0, f.Bus.ActiveCount);
        Task second = f.Engine.ExecuteRecipeAsync(recipe);
        Assert.NotEqual(sent[0].RecipeStepId, sent[1].RecipeStepId);
        f.Bus.Emit("agent.result.capture.A", Result(sent[0], "stale"));
        Assert.False(second.IsCompleted);
        f.Bus.Emit("agent.result.capture.A", Result(sent[1], "second"));
        await second;
        Assert.Equal(new[] { "first.y16", "second.y16" }, f.History.Select(record => record.AgentRawPath));
        Assert.Equal(0, f.Bus.ActiveCount);
    }

    [Fact]
    public async Task Shutdown_CancelsPendingCaptureAndDrainsTargetedAbort()
    {
        var f = new Fixture();
        CaptureCommandMessage? capture = null;
        f.Bus.Nats.Setup(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
            .Callback<CaptureCommandMessage>(command => capture = command).Returns(Task.CompletedTask);
        Task run = f.Engine.ExecuteRecipeAsync(new Recipe { Steps = new() { CameraStep(CameraControlOps.Capture), MoveStep() } });
        CaptureCommandMessage request = Assert.IsType<CaptureCommandMessage>(capture);
        await f.Engine.CancelActiveRunAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        CameraControlMessage abort = Assert.Single(f.Controls);
        Assert.Equal(CameraControlOps.CaptureAbort, abort.Op);
        Assert.Equal(request.RecipeStepId, abort.TargetRequestId);
        Assert.Equal(0, f.Bus.ActiveCount);
        f.Plc.Verify(service => service.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public async Task DashboardDispose_ReleasesLiveAndStatusSubscriptions()
    {
        var bus = new NatsScopeFixtureDeepAudit();
        var vm = new DashboardViewModel(null, bus.Nats.Object, () => Array.Empty<Recipe>(), false);
        Assert.Equal(2, bus.ActiveCount);
        await vm.DisposeAsync();
        Assert.Equal(0, bus.ActiveCount);
        bus.Emit("agent.status.A", new AgentStatusMessage { AgentId = "A", CameraStatus = CameraStatus.Connected });
        Assert.Empty(vm.Agents);
    }

    private static RecipeStep CameraStep(string operation) => new()
    {
        Kind = RecipeStepKind.CameraCommand, CameraOperation = operation,
        CameraTargets = new() { new RecipeCameraTarget { AgentId = "A", CameraIndex = 4 } }
    };
    private static RecipeStep MoveStep() => new() { Kind = RecipeStepKind.MotorMove, PositionX = 10, PositionY = 20 };
    private static CaptureResultMessage Result(CaptureCommandMessage command, string id) => new()
    {
        AgentId = command.TargetAgentId, CameraIndex = 4, RecipeStepId = command.RecipeStepId,
        CaptureId = id, IsSuccess = true, ImagePath = $"{id}.y16", Timestamp = DateTime.UtcNow
    };

    private sealed class Fixture
    {
        public NatsScopeFixtureDeepAudit Bus { get; } = new();
        public Mock<IPlcController> Plc { get; } = new();
        public List<CaptureHistoryRecord> History { get; } = new();
        public List<CameraControlMessage> Controls { get; } = new();
        public List<RecipeStepExecution> TerminalEvents { get; } = new();
        public RecipeEngine Engine { get; }
        public Fixture()
        {
            Plc.Setup(controller => controller.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot());
            var history = new Mock<ICaptureHistoryRepository>();
            history.Setup(repository => repository.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Callback<CaptureHistoryRecord>(History.Add).Returns(Task.CompletedTask);
            Bus.Nats.Setup(service => service.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
                .Callback<CameraControlMessage>(Controls.Add).Returns(Task.CompletedTask);
            Engine = new RecipeEngine(Plc.Object, Bus.Nats.Object, history.Object, new RecipeEngineSettings { CaptureResultTimeoutSeconds = 2 });
            Engine.StepExecutionChanged += (_, result) => { if (result.Outcome != RecipeStepOutcome.Started) TerminalEvents.Add(result); };
        }
    }
}
