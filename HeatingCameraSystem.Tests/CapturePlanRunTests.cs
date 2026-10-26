using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CapturePlanRunTests
{
    [Fact]
    public async Task FailedCaptureKeepsCellFailedAndResumeRepeatsWholeCameraExpansion()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db);
            foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var source = ExecutableTemplate(); var plan = TemplatePlanTestData.Plan(source); var recipes = new FileRecipeRepository(root); await recipes.SaveAsync(source);
            plan.SaveRootPath = Path.Combine(root, "captures");
            var runs = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(runs, positions, recipes, () => TemplatePlanTestData.Cameras);
            var first = await service.PrepareAsync(plan, CapturePlanRunMode.New);
            bool failed = false;
            int failedCameraCaptures = 0;
            await Assert.ThrowsAsync<RecipeNgException>(() => service.ExecuteAsync(first, Engine(cmd =>
            {
                if (cmd.TargetAgentId == TemplatePlanTestData.Cameras[1].AgentId)
                {
                    failedCameraCaptures++;
                    if (!failed) { failed = true; return false; }
                }
                return true;
            }), CancellationToken.None));
            Assert.Equal(1, failedCameraCaptures);
            Assert.Equal(CapturePlanRunOutcome.Failed, first.Run.Outcome);
            Assert.Equal(1, first.Run.Cells.Count(c => c.State == CapturePlanCellState.Done)); Assert.Equal(1, first.Run.Cells.Count(c => c.State == CapturePlanCellState.Failed));
            var retry = await service.PrepareAsync(plan, CapturePlanRunMode.FailedOnly); Assert.Equal(1, retry.CellCount); Assert.Equal(10, retry.Recipe.Steps.Count);
            Assert.Equal(RecipeStepKind.ChamberControl, retry.Recipe.Steps[0].Kind); Assert.Equal(RecipeStepKind.BlackBodyControl, retry.Recipe.Steps[1].Kind);
            Assert.Equal(first.Recipe.Steps.Skip(10).Select(s => s.StepId), retry.Recipe.Steps.Select(s => s.StepId));
            await service.ExecuteAsync(retry, Engine(_ => true), CancellationToken.None);
            var board = await service.LoadBoardAsync(plan); Assert.Equal(2, board.DoneCells); Assert.Equal(0, board.FailedCells);
            Assert.Equal(first.Run.Id, retry.Run.ParentRunId); Assert.Equal(first.Run.RootRunId, retry.Run.RootRunId); Assert.Equal(2, (await runs.GetLatestChainAsync(plan.Id)).Count);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task EditedTemplateResumeWarnsAndKeepsDoneCellsButUsesCurrentSteps()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-run-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db); foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source); var recipes = new FileRecipeRepository(root); await recipes.SaveAsync(source);
            var runs = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(runs, positions, recipes);
            var original = await service.PrepareAsync(plan, CapturePlanRunMode.New); original.Run.Cells[0].State = CapturePlanCellState.Done; original.Run.Cells[1].State = CapturePlanCellState.Failed; await runs.SaveAsync(original.Run);
            source.Steps[3].WaitDurationSeconds = 7; await recipes.SaveAsync(source);
            var board = await service.LoadBoardAsync(plan); Assert.Contains("템플릿이 이전 시도 이후 변경됨", board.Warning); Assert.Equal(1, board.DoneCells);
            var resume = await service.PrepareAsync(plan, CapturePlanRunMode.Remaining); Assert.Contains("변경됨", resume.Warning); Assert.Equal(1, resume.CellCount); Assert.Equal(7, resume.Recipe.Steps[3].WaitDurationSeconds); Assert.Equal(1800, original.Recipe.Steps[3].WaitDurationSeconds);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void CellDoneRequiresEveryStepIncludingCommandsAfterLastCapture()
    {
        var cell = new CapturePlanRunCell { Steps = new() { new() { StepId = "capture", Kind = RecipeStepKind.CameraCommand }, new() { StepId = "nuc", Kind = RecipeStepKind.CameraCommand } } };
        CapturePlanRunService.ApplyStep(cell, new RecipeStepExecution { StepId = "capture", Outcome = RecipeStepOutcome.Succeeded }); Assert.Equal(CapturePlanCellState.Running, cell.State);
        CapturePlanRunService.ApplyStep(cell, new RecipeStepExecution { StepId = "nuc", Outcome = RecipeStepOutcome.Failed }); Assert.Equal(CapturePlanCellState.Failed, cell.State);
    }
    [Fact]
    public async Task OldRunReceiptsAreSafelyIgnoredAndNewRunningReceiptsRecover()
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCapturePlanRunRepository(db);
        await repo.SaveAsync(new CapturePlanRunRecord { Id = "old", RootRunId = "old", RecipeId = "plan", SchemaVersion = 0 }); Assert.Empty(await repo.GetLatestChainAsync("plan"));
        await repo.SaveAsync(new CapturePlanRunRecord { Id = "new", RootRunId = "new", RecipeId = "plan", SchemaVersion = 2, Outcome = CapturePlanRunOutcome.Running, Cells = new() { new() { CellKey = "started", State = CapturePlanCellState.Running }, new() { CellKey = "pending", State = CapturePlanCellState.Pending } } });
        await repo.RecoverInterruptedAsync(); var restored = (await repo.GetLatestChainAsync("plan")).Single(); Assert.Equal(CapturePlanRunOutcome.Interrupted, restored.Outcome); Assert.Equal(CapturePlanCellState.Failed, restored.Cells[0].State); Assert.Equal(CapturePlanCellState.Pending, restored.Cells[1].State);
    }
    private static Recipe ExecutableTemplate()
    {
        var source = TemplatePlanTestData.Template(); source.Steps[0].WaitForChamberStabilization = false; source.Steps[1].WaitForStabilization = false; source.Steps[3].WaitDurationSeconds = 0;
        foreach (var step in source.Steps.Where(s => s.CameraOperation == CameraControlOps.Capture)) step.ShotCount = 1;
        return source;
    }
    internal static RecipeEngine Engine(Func<CaptureCommandMessage, bool> succeeds)
    {
        int point = 0;
        var plc = new Mock<IPlcController>(); plc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(10); plc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50);
        plc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(() => new PlcStatusSnapshot { CurrentPoint = point });
        plc.Setup(p => p.MoveServoToPositionAsync(It.IsAny<int>())).Callback<int>(target => point = target).Returns(Task.CompletedTask);
        var history = new Mock<ICaptureHistoryRepository>(); history.Setup(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
        var nats = new Mock<INatsCommunicationService>(); Action<CaptureResultMessage>? callback = null; var acks = new Dictionary<string, Action<CameraControlAckMessage>>();
        nats.Setup(s => s.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Callback<Action<CaptureResultMessage>>(handler => callback = handler).Returns(Task.CompletedTask);
        nats.Setup(s => s.SubscribeCameraControlAckAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlAckMessage>>())).Callback<string, Action<CameraControlAckMessage>>((id, handler) => acks[id] = handler).Returns(Task.CompletedTask);
        nats.Setup(s => s.PublishCameraControlAsync(It.IsAny<CameraControlMessage>())).Callback<CameraControlMessage>(cmd =>
        {
            if (!acks.TryGetValue(cmd.AgentId, out var ack)) return;
            bool bias = BiasExecutionPolicy.IsBiasOperation(cmd.Op) || cmd.Op == CameraControlOps.BiasUseCurrent;
            var storage = cmd.BiasStorage;
            ack(new CameraControlAckMessage
            {
                AgentId = cmd.AgentId, CameraIndex = cmd.CameraIndex, Op = cmd.Op, RequestId = cmd.RequestId, IsSuccess = true,
                BiasFileSaved = bias, Attempts = bias ? 1 : 0,
                BiasFilePath = bias && storage is not null
                    ? Path.Combine(storage.StorageRootUnc, $"544112136_{storage.ProductNumber}", storage.ConditionFolder, "bias.json") : string.Empty,
                BiasApplicationId = bias ? $"applied-{cmd.AgentId}" : string.Empty,
                BiasSourceOperation = cmd.Op == CameraControlOps.BiasUseCurrent ? cmd.BiasSourceOperation : cmd.Op
            });
        }).Returns(Task.CompletedTask);
        nats.Setup(s => s.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>())).Callback<CaptureCommandMessage>(cmd => callback?.Invoke(new() { AgentId = cmd.TargetAgentId, CameraIndex = CaptureReplyFixtureDeepAudit.CameraIndex(cmd), RecipeStepId = cmd.RecipeStepId, IsSuccess = succeeds(cmd), ImagePath = "/test/image.jpg", Timestamp = DateTime.UtcNow })).Returns(Task.CompletedTask);
        return new RecipeEngine(plc.Object, nats.Object, history.Object, new RecipeEngineSettings { CaptureResultTimeoutSeconds = 1 });
    }
}
