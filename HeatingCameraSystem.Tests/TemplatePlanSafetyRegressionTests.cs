using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class TemplatePlanSafetyRegressionTests
{
    [Theory]
    [InlineData("agent")][InlineData("index")][InlineData("role")][InlineData("kind")]
    [InlineData("nan-x")][InlineData("infinity-y")][InlineData("point-zero")][InlineData("point-one")]
    public async Task InvalidPositionRulesRejectWithoutPersistence(string invalid)
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db);
        var entry = TemplatePlanTestData.Positions()[0];
        switch (invalid)
        {
            case "agent": entry.AgentId = ""; break;
            case "index": entry.CameraIndex = -1; break;
            case "role": entry.Role = (BlackBodyRole)999; break;
            case "kind": entry.Position.Kind = (CameraPositionKind)999; break;
            case "nan-x": entry.Position.X = float.NaN; break;
            case "infinity-y": entry.Position.Y = float.PositiveInfinity; break;
            case "point-zero": entry.Position.PointNumber = 0; break;
            case "point-one": entry.Position.PointNumber = 1; break;
        }
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SaveAsync(entry)); Assert.Empty(await repo.GetAllAsync());
    }
    [Fact]
    public async Task PositionSaveErrorRemainsVisibleAndDoesNotClearEditedState()
    {
        var repo = new Mock<ICameraPositionRepository>(); repo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<CameraPositionEntry>());
        repo.Setup(r => r.SaveAsync(It.IsAny<CameraPositionEntry>())).ThrowsAsync(new InvalidOperationException("storage unavailable"));
        var vm = new CameraPositionsViewModel(() => repo.Object, () => null, () => 20, () => false, (_, _) => true, () => TemplatePlanTestData.Cameras);
        await vm.LoadAsync(); var row = vm.Rows[0]; row.X = 15; await vm.SaveCommand.ExecuteAsync(row);
        Assert.True(row.IsEdited); Assert.False(row.IsPersisted); Assert.Contains("storage unavailable", vm.StatusMessage);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task WriteToPointRequiresConfirmationThenPersistsAndNotifies(bool confirm)
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db); var plc = new Mock<IPlcController>(); plc.SetupGet(p => p.IsConnected).Returns(true);
        var vm = new CameraPositionsViewModel(() => repo, () => plc.Object, () => 20, () => false, (_, _) => confirm, () => TemplatePlanTestData.Cameras);
        await vm.LoadAsync(); var row = vm.Rows[0]; row.X = 5; row.Y = 7; row.WriteTargetPoint = 8; int events = 0; vm.PositionsChanged += (_, _) => events++;
        await vm.WriteToPointCommand.ExecuteAsync(row);
        plc.Verify(p => p.SetPointCoordinateAsync(8, 5, 7), confirm ? Times.Once() : Times.Never());
        Assert.Equal(confirm ? 1 : 0, events); Assert.Equal(confirm, row.IsPersisted);
        if (confirm) { Assert.True(row.IsKindLocked); Assert.Single(vm.RegisteredPositions); }
    }
    [Fact]
    public async Task CancelledRunIsDurableBeforeCancellationReturns()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-cancel-regression-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var recipes = new FileRecipeRepository(root); var source = TemplatePlanTestData.Template(); await recipes.SaveAsync(source);
            var positions = new LiteDbCameraPositionRepository(db); foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var runs = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(runs, positions, recipes); var prepared = await service.PrepareAsync(TemplatePlanTestData.Plan(source), CapturePlanRunMode.New);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(prepared, CapturePlanRunTests.Engine(_ => true), cancellation.Token));
            Assert.Equal(CapturePlanRunOutcome.Cancelled, Assert.IsType<CapturePlanRunRecord>(await runs.GetByIdAsync(prepared.Run.Id)).Outcome);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("file")][InlineData("db")][InlineData("csv")]
    public async Task InvalidDraftRoundTripsWithoutCachedSteps(string storage)
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-draft-regression-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var draft = TemplatePlanTestData.Plan(TemplatePlanTestData.Template()); draft.PlanValidationError = "실행 불가: 위치 없음";
            Recipe loaded;
            if (storage == "csv") loaded = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(draft));
            else
            {
                IRecipeRepository repo = storage == "file" ? new FileRecipeRepository(root) : new LiteDbRecipeRepository(db);
                await repo.SaveAsync(draft); loaded = Assert.IsType<Recipe>(await repo.GetByIdAsync(draft.Id));
            }
            Assert.Empty(loaded.Steps); Assert.Equal(draft.PlanValidationError, loaded.PlanValidationError); Assert.Equal(2, TemplatePlanTestData.Entries(loaded)[0].Cameras.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void FailedPositionReloadCannotReuseStaleSnapshot() => PlanSta.Run(async () =>
    {
        var source = TemplatePlanTestData.Template();
        var recipes = new Mock<IRecipeRepository>(); recipes.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { source });
        var positions = new Mock<ICameraPositionRepository>();
        positions.SetupSequence(p => p.GetAllAsync()).ReturnsAsync(TemplatePlanTestData.Positions()).ThrowsAsync(new InvalidOperationException("positions offline"));
        using var editor = new CapturePlanEditorViewModel(TemplatePlanTestData.Plan(source), positions.Object, recipes.Object, () => TemplatePlanTestData.Cameras);
        await editor.LoadPositionsAsync(); Assert.True(editor.TryCompile(out _));
        await editor.LoadPositionsAsync(); Assert.Empty(editor.PositionSnapshot); Assert.False(editor.TryCompile(out var steps));
        Assert.Empty(steps); Assert.Contains("positions offline", editor.ValidationMessage);
    });
    [Theory]
    [InlineData("missing-position")][InlineData("cancel")][InlineData("no-dialog")][InlineData("recovery")]
    public async Task DashboardGuardsNeverStartOrPersistAnUnconfirmedRetry(string guard)
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-dashboard-guard-" + Guid.NewGuid().ToString("N"));
        var engineProperty = typeof(AppServices).GetProperty(nameof(AppServices.RecipeEngine)) ?? throw new InvalidOperationException();
        var recipeProperty = typeof(AppServices).GetProperty(nameof(AppServices.RecipeRepo)) ?? throw new InvalidOperationException();
        object? oldEngine = engineProperty.GetValue(null), oldRecipes = recipeProperty.GetValue(null);
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var recipes = new FileRecipeRepository(root);
            var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
            await recipes.SaveAsync(source); await recipes.SaveAsync(plan);
            var positions = new LiteDbCameraPositionRepository(db); foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var runs = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(runs, positions, recipes);
            var previous = await service.PrepareAsync(plan, CapturePlanRunMode.New);
            foreach (var cell in previous.Run.Cells) cell.State = CapturePlanCellState.Failed;
            await runs.SaveAsync(previous.Run);
            if (guard == "missing-position") await positions.DeleteAsync(TemplatePlanTestData.Cameras[0], BlackBodyRole.Cold);
            var engine = CapturePlanRunTests.Engine(_ => true); engineProperty.SetValue(null, engine); recipeProperty.SetValue(null, recipes);
            var dialogs = new Mock<IDialogService>();
            dialogs.Setup(d => d.PromptProductionRun(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProductionCaptureFormat>(), It.IsAny<bool>())).Returns(new ProductionRunInput("", "product", ProductionCaptureFormat.Raw));
            dialogs.Setup(d => d.ConfirmCapturePlanRun(It.IsAny<string>())).Returns(false);
            using var dashboard = new DashboardViewModel(null, null, () => new[] { plan }, false, dialogService: guard == "no-dialog" ? null : dialogs.Object, planRunService: service);
            dashboard.SelectedRecipe = plan; await dashboard.ReloadPlanBoardAsync();
            if (guard == "recovery") dashboard.RecoveryLockActive = true;
            await dashboard.RetryFailedCellsCommand.ExecuteAsync(null);
            Assert.Null(engine.CurrentRunId); Assert.Single(await runs.GetLatestChainAsync(plan.Id));
            if (guard == "cancel") dialogs.Verify(d => d.ConfirmCapturePlanRun(It.Is<string>(s => s.Contains("2개"))), Times.Once);
        }
        finally
        {
            engineProperty.SetValue(null, oldEngine); recipeProperty.SetValue(null, oldRecipes);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
