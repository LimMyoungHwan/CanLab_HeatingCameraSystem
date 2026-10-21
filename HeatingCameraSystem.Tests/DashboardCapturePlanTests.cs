using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class DashboardCapturePlanTests
{
    [Fact]
    public async Task DashboardReloadsWholeCameraCellsFromPersistentRunChain()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-dashboard-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var recipes = new FileRecipeRepository(root); var source = TemplatePlanTestData.Template(); await recipes.SaveAsync(source);
            var plan = TemplatePlanTestData.Plan(source); var positions = new LiteDbCameraPositionRepository(db); foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var repository = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(repository, positions, recipes);
            var prepared = await service.PrepareAsync(plan, CapturePlanRunMode.New); prepared.Run.Cells[0].State = CapturePlanCellState.Done; prepared.Run.Cells[1].State = CapturePlanCellState.Failed; await repository.SaveAsync(prepared.Run);
            using var first = new DashboardViewModel(null, null, () => new[] { plan }, false, planRunService: service); first.SelectedRecipe = plan; await first.ReloadPlanBoardAsync();
            var firstBoard = Assert.IsType<CapturePlanBoard>(first.PlanBoard); Assert.True(first.HasCapturePlan); Assert.True(first.IsPlanBoardVisible); Assert.Equal("완료 1 / 2 · 실패 1", firstBoard.Summary); Assert.True(first.ResumeRemainingCommand.CanExecute(null)); Assert.True(first.RetryFailedCellsCommand.CanExecute(null));
            using var reopened = new DashboardViewModel(null, null, () => new[] { plan }, false, planRunService: service); reopened.SelectedRecipe = plan; await reopened.ReloadPlanBoardAsync();
            var reopenedBoard = Assert.IsType<CapturePlanBoard>(reopened.PlanBoard); Assert.Equal(firstBoard.Summary, reopenedBoard.Summary); Assert.Equal(2, reopenedBoard.Cells.Count());
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task OldPlanBoardIsReadableBlockedMessageRatherThanMatrixCrash()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-dashboard-old-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var service = new CapturePlanRunService(new LiteDbCapturePlanRunRepository(db), new LiteDbCameraPositionRepository(db), new FileRecipeRepository(root));
            var plan = new Recipe { Plan = new CapturePlan() }; using var dashboard = new DashboardViewModel(null, null, () => new[] { plan }, false, planRunService: service);
            dashboard.SelectedRecipe = plan; await dashboard.ReloadPlanBoardAsync(); var board = Assert.IsType<CapturePlanBoard>(dashboard.PlanBoard); Assert.Equal(CapturePlanCompiler.OldFormatMessage, board.Warning); Assert.Empty(board.Entries); Assert.False(dashboard.ResumeRemainingCommand.CanExecute(null));
        }
        finally { Directory.Delete(root, true); }
    }
}
