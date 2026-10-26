using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class RecipeEngineOutcomeTests
{
    [Fact]
    public async Task ExplicitRunId_MatchesEventsHistoryAndRequestPrefix()
    {
        var fixture = new Fixture();
        var step = CaptureStep();
        const string runId = "persisted-plan-attempt";

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(step), runId: runId);

        Assert.Equal(runId, fixture.Engine.CurrentRunId);
        var history = Assert.Single(fixture.History);
        Assert.Equal(runId, history.RunId);
        Assert.StartsWith(step.StepId + ":Agent_7:7:", history.RecipeStepId);
        Assert.Equal(step.StepId, RecipeCaptureRequestKey.GetStepId(history.RecipeStepId));
        Assert.Collection(fixture.Events,
            started => Assert.Equal(RecipeStepOutcome.Started, started.Outcome),
            ended => Assert.Equal(RecipeStepOutcome.Succeeded, ended.Outcome));
        Assert.All(fixture.Events, execution =>
        {
            Assert.Equal(runId, execution.RunId);
            Assert.Equal(step.StepId, execution.StepId);
            Assert.Equal(step.PlanCellKey, execution.PlanCellKey);
            Assert.Equal(0, execution.StepIndex);
            Assert.Equal(7, execution.CameraIndex);
            Assert.Equal(RecipeStepKind.CameraCommand, execution.Kind);
        });
        Assert.Equal(runId, Assert.Single(fixture.RunIdsAtPublish));
    }

    [Fact]
    public async Task OmittedRunId_GeneratesIdentitySharedWithHistory()
    {
        var fixture = new Fixture();
        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(CaptureStep()));

        Assert.True(Guid.TryParse(fixture.Engine.CurrentRunId, out _));
        Assert.Equal(fixture.Engine.CurrentRunId, Assert.Single(fixture.History).RunId);
        Assert.All(fixture.Events, execution => Assert.Equal(fixture.Engine.CurrentRunId, execution.RunId));
    }

    [Fact]
    public async Task PartialBatch_StoresSuccessfulPartButEmitsFailed()
    {
        var fixture = new Fixture();
        var step = CaptureStep();
        step.ShotCount = 2;
        fixture.Reply = (command, emit) => emit(Result(command));

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(step));

        Assert.Single(fixture.History);
        var ended = Assert.Single(fixture.TerminalEvents);
        Assert.Equal(RecipeStepOutcome.Failed, ended.Outcome);
        Assert.Contains("1 of 2", ended.Reason);
    }

    [Fact]
    public async Task OfflineOperatorSkip_EmitsSkippedNotSucceeded()
    {
        var fixture = new Fixture(new AgentDirectory());

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(CaptureStep()),
            waitForResumeAsync: _ => Task.FromResult(true));

        Assert.Empty(fixture.Commands);
        Assert.Empty(fixture.History);
        Assert.Equal(RecipeStepOutcome.Skipped, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    [Fact]
    public async Task MixedTargets_IntentionalSkipDoesNotBecomeSuccessForWholeStep()
    {
        var directory = new AgentDirectory();
        directory.Note(new AgentStatusMessage { AgentId = "Agent_7", CameraStatus = CameraStatus.Connected });
        var fixture = new Fixture(directory);
        var step = CaptureStep();
        step.CameraTargets.Add(new RecipeCameraTarget { CameraIndex = 7 });
        step.CameraTargets.Add(new RecipeCameraTarget { CameraIndex = 8 });

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(step),
            waitForResumeAsync: _ => Task.FromResult(true));

        Assert.Equal("Agent_7", Assert.Single(fixture.Commands).TargetAgentId);
        Assert.Single(fixture.History);
        var ended = Assert.Single(fixture.TerminalEvents);
        Assert.Equal(RecipeStepOutcome.Skipped, ended.Outcome);
        Assert.Contains("8", ended.Reason);
    }

    [Fact]
    public async Task RepeatedCapture_OneFailedRoundCannotBeOverwrittenByLaterSuccess()
    {
        var fixture = new Fixture();
        var step = CaptureStep();
        step.CaptureIntervalSeconds = 1;
        step.CaptureDurationSeconds = 2;
        fixture.Reply = (command, emit) => emit(Result(command, !command.RecipeStepId.EndsWith(":0", StringComparison.Ordinal)));

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(step));

        Assert.Equal(2, fixture.Commands.Count);
        Assert.Single(fixture.History);
        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    [Fact]
    public async Task RepeatedCapture_PublishFailureRemainsFailedAfterLaterSuccess()
    {
        var fixture = new Fixture();
        var step = CaptureStep();
        step.CaptureIntervalSeconds = 1;
        step.CaptureDurationSeconds = 2;
        fixture.Reply = (command, emit) =>
        {
            if (command.RecipeStepId.EndsWith(":0", StringComparison.Ordinal))
                throw new InvalidOperationException("publish unavailable");
            emit(Result(command));
        };

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(step));

        Assert.Single(fixture.History);
        var ended = Assert.Single(fixture.TerminalEvents);
        Assert.Equal(RecipeStepOutcome.Failed, ended.Outcome);
        Assert.Contains("publish unavailable", ended.Reason);
    }

    [Fact]
    public async Task ForkCapture_FinalizesAtJoinAfterHistoryStorage()
    {
        var fixture = new Fixture();
        var capture = CaptureStep();
        capture.WaitForCaptureResult = false;
        var join = new RecipeStep { Kind = RecipeStepKind.CaptureJoin };
        fixture.Engine.StepExecutionChanged += (_, execution) =>
        {
            if (execution.StepId == join.StepId && execution.Outcome == RecipeStepOutcome.Started)
            {
                Assert.Empty(fixture.TerminalEvents.Where(e => e.StepId == capture.StepId));
                Assert.Empty(fixture.History);
            }
            if (execution.StepId == capture.StepId && execution.Outcome == RecipeStepOutcome.Succeeded)
                Assert.Single(fixture.History);
        };

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(capture, join));

        Assert.Equal(RecipeStepOutcome.Succeeded,
            Assert.Single(fixture.TerminalEvents.Where(e => e.StepId == capture.StepId)).Outcome);
    }

    [Fact]
    public async Task UnjoinedFork_NeverReportsSuccess()
    {
        var fixture = new Fixture();
        var capture = CaptureStep();
        capture.WaitForCaptureResult = false;

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(capture));

        Assert.Empty(fixture.History);
        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    [Fact]
    public async Task HistoryStorageFailure_EmitsFailedAndPropagates()
    {
        var fixture = new Fixture();
        fixture.HistoryRepository.Setup(repository => repository.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
            .ThrowsAsync(new InvalidOperationException("history unavailable"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Engine.ExecuteRecipeAsync(RecipeWith(CaptureStep())));

        Assert.Equal("history unavailable", error.Message);
        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    [Fact]
    public async Task ReceiptSubscriberFailure_IsNotSwallowed()
    {
        var fixture = new Fixture();
        fixture.Engine.StepExecutionChanged += (_, execution) =>
        {
            if (execution.Outcome == RecipeStepOutcome.Succeeded)
                throw new InvalidOperationException("receipt unavailable");
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Engine.ExecuteRecipeAsync(RecipeWith(CaptureStep())));

        Assert.Equal("receipt unavailable", error.Message);
    }

    [Fact]
    public async Task CancelledGenericStep_EmitsFailedTerminalReceipt()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Engine.StepExecutionChanged += (_, execution) =>
        {
            if (execution.Outcome == RecipeStepOutcome.Started) cancellation.Cancel();
        };
        var wait = new RecipeStep { Kind = RecipeStepKind.Wait, WaitDurationSeconds = 60 };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Engine.ExecuteRecipeAsync(RecipeWith(wait), cancellation.Token));

        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    [Fact]
    public async Task ForkCapture_PartialFailureFinalizesFailedAtJoin()
    {
        var fixture = new Fixture();
        var capture = CaptureStep();
        capture.ShotCount = 2;
        capture.WaitForCaptureResult = false;
        fixture.Reply = (command, emit) =>
        {
            emit(Result(command));
            emit(Result(command, false));
        };
        var join = new RecipeStep { Kind = RecipeStepKind.CaptureJoin };

        await fixture.Engine.ExecuteRecipeAsync(RecipeWith(capture, join));

        Assert.Single(fixture.History);
        Assert.Equal(RecipeStepOutcome.Failed,
            Assert.Single(fixture.TerminalEvents.Where(e => e.StepId == capture.StepId)).Outcome);
    }

    [Fact]
    public async Task CancellationWhileWaitingForCapture_NeverEmitsSucceeded()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Reply = (_, _) => cancellation.Cancel();

        // Cancellation must leave the run cancelled and stop the outstanding remote capture.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Engine.ExecuteRecipeAsync(RecipeWith(CaptureStep()), cancellation.Token));

        Assert.Empty(fixture.History);
        Assert.Equal(RecipeStepOutcome.Failed, Assert.Single(fixture.TerminalEvents).Outcome);
    }

    private static RecipeStep CaptureStep() => new()
    {
        Kind = RecipeStepKind.CameraCommand,
        CameraOperation = CameraControlOps.Capture,
        CameraIndex = 7,
        PlanCellKey = "condition:7:Hot",
        ShotCount = 1
    };

    private static Recipe RecipeWith(params RecipeStep[] steps) => new() { Steps = steps.ToList() };

    private static CaptureResultMessage Result(CaptureCommandMessage command, bool success = true) => new()
    {
        AgentId = command.TargetAgentId,
        CameraIndex = CaptureReplyFixtureDeepAudit.CameraIndex(command),
        RecipeStepId = command.RecipeStepId,
        IsSuccess = success,
        ImagePath = "/test/outcome.y16",
        Timestamp = DateTime.UtcNow
    };

    private sealed class Fixture
    {
        public Mock<ICaptureHistoryRepository> HistoryRepository { get; } = new();
        public List<CaptureHistoryRecord> History { get; } = new();
        public List<CaptureCommandMessage> Commands { get; } = new();
        public List<string?> RunIdsAtPublish { get; } = new();
        public List<RecipeStepExecution> Events { get; } = new();
        public IEnumerable<RecipeStepExecution> TerminalEvents => Events.Where(e => e.Outcome != RecipeStepOutcome.Started);
        public Action<CaptureCommandMessage, Action<CaptureResultMessage>> Reply { get; set; }
            = (command, emit) => emit(Result(command));
        public RecipeEngine Engine { get; }

        public Fixture(AgentDirectory? directory = null)
        {
            var plc = new Mock<IPlcController>();
            plc.Setup(controller => controller.GetCurrentTemperatureAsync()).ReturnsAsync(25f);
            plc.Setup(controller => controller.GetCurrentHumidityAsync()).ReturnsAsync(50f);
            HistoryRepository.Setup(repository => repository.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Callback<CaptureHistoryRecord>(History.Add).Returns(Task.CompletedTask);
            var nats = new Mock<INatsCommunicationService>();
            Action<CaptureResultMessage> emit = _ => throw new InvalidOperationException("Not subscribed.");
            nats.Setup(service => service.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(callback => emit = callback).Returns(Task.CompletedTask);
            Engine = new RecipeEngine(plc.Object, nats.Object, HistoryRepository.Object,
                new RecipeEngineSettings { CaptureResultTimeoutSeconds = 0 }, agentDirectory: directory);
            Engine.StepExecutionChanged += (_, execution) => Events.Add(execution);
            nats.Setup(service => service.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                .Callback<CaptureCommandMessage>(command =>
                {
                    Commands.Add(command);
                    RunIdsAtPublish.Add(Engine.CurrentRunId);
                    Reply(command, emit);
                }).Returns(Task.CompletedTask);
        }
    }
}
