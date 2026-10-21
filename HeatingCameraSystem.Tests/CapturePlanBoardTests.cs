using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CapturePlanBoardTests
{
    [Fact]
    public async Task BoardShowsSavedOfflineCameraAndPerCaptureRoleStatuses()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-board-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var recipes = new FileRecipeRepository(root); var source = TemplatePlanTestData.Template(); await recipes.SaveAsync(source);
            var plan = TemplatePlanTestData.Plan(source); var runs = new LiteDbCapturePlanRunRepository(db); var positions = new LiteDbCameraPositionRepository(db);
            foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            var service = new CapturePlanRunService(runs, positions, recipes, () => new[] { TemplatePlanTestData.Cameras[0] }); var prepared = await service.PrepareAsync(plan, CapturePlanRunMode.New);
            prepared.Run.Cells[0].State = CapturePlanCellState.Done; prepared.Run.Cells[1].State = CapturePlanCellState.Failed;
            var capture = prepared.Run.Cells[1].Steps.First(s => s.Operation == CameraControlOps.Capture && s.Kind == RecipeStepKind.CameraCommand); capture.Outcome = RecipeStepOutcome.Failed; capture.Reason = "capture failed";
            await runs.SaveAsync(prepared.Run); var board = await service.LoadBoardAsync(plan);
            Assert.Equal("완료 1 / 2 · 실패 1", board.Summary); var entry = Assert.Single(board.Entries); Assert.Contains("MP10", entry.Label); Assert.DoesNotContain("entry", entry.Label);
            Assert.True(entry.Cells[0].IsOnline); Assert.False(entry.Cells[1].IsOnline); Assert.Contains("오프라인", entry.Cells[1].CameraLabel);
            Assert.Equal(4, entry.Cells[1].Captures.Count()); Assert.Contains(entry.Cells[1].Captures, s => s.Role == BlackBodyRole.Cold && s.Outcome == RecipeStepOutcome.Failed);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(CapturePlanCellState.Pending, "대기")][InlineData(CapturePlanCellState.Running, "진행")][InlineData(CapturePlanCellState.Done, "완료")][InlineData(CapturePlanCellState.Failed, "실패")][InlineData(CapturePlanCellState.Skipped, "건너뜀")]
    public void StatesUseTextNotOnlyColor(CapturePlanCellState state, string expected)
        => Assert.Equal(expected, new CapturePlanBoardCell { State = state }.Label);
}
