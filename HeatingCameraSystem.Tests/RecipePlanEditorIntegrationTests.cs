using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class RecipePlanEditorIntegrationTests
{
    [Fact]
    public void MakePlanLinksRecipeAndInvalidDraftSavesWithoutCachedSteps() => PlanSta.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-editor-link-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db); var repo = new FileRecipeRepository(root);
            var source = TemplatePlanTestData.Template(); await repo.SaveAsync(source);
            using var editor = new RecipeEditorViewModel(repo, positions); editor.ConvertToPlanCommand.Execute(null);
            var created = Assert.IsType<RecipeModel>(editor.SelectedRecipe); Assert.True(created.IsPlanRecipe);
            var entry = Assert.Single(Assert.IsType<List<CapturePlanEntry>>(Assert.IsType<CapturePlan>(created.Plan).Entries));
            Assert.Equal(source.Id, entry.TemplateRecipeId); Assert.Empty(entry.Cameras);
            editor.SaveRecipeCommand.Execute(null); var saved = Assert.IsType<Recipe>(await repo.GetByIdAsync(created.Id));
            Assert.Empty(saved.Steps); Assert.Contains("실행 불가", saved.PlanValidationError); Assert.Equal(10, Assert.IsType<Recipe>(await repo.GetByIdAsync(source.Id)).Steps.Count);
            editor.SelectRecipeCommand.Execute(editor.Recipes.Single(r => r.Id == source.Id)); Assert.Contains(created.Name, editor.TemplateUsage);
            editor.DeleteRecipeCommand.Execute(editor.SelectedRecipe); Assert.Contains(created.Name, editor.OperationError); Assert.Equal(2, editor.Recipes.Count);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public void PositionSlotInfersNextCaptureRoleAndIndependentDefaultsSurviveSave() => PlanSta.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-editor-slots-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = TemplatePlanTestData.Template(); source.Steps.ForEach(s => { s.UseCellCamera = false; s.CellPositionRole = null; });
            var repo = new FileRecipeRepository(root); await repo.SaveAsync(source);
            using var db = new LiteDatabase(new MemoryStream()); using var editor = new RecipeEditorViewModel(repo, new LiteDbCameraPositionRepository(db));
            var selected = Assert.IsType<RecipeModel>(editor.SelectedRecipe);
            var step = selected.Steps[2]; Assert.False(step.UsesCellPosition); editor.TogglePositionSlotCommand.Execute(step); Assert.Equal(BlackBodyRole.Cold, step.CellPositionRole);
            selected.Steps[5].UseCellCamera = true; editor.SaveRecipeCommand.Execute(null);
            var stored = Assert.IsType<Recipe>(await repo.GetByIdAsync(source.Id)); Assert.Equal(12, stored.Steps[2].TargetPositionIndex); Assert.Equal(TemplatePlanTestData.Cameras[0].AgentId, stored.Steps[5].CameraTargets[0].AgentId);
            Assert.True(stored.Steps[5].UseCellCamera); Assert.Equal(BlackBodyRole.Cold, stored.Steps[2].CellPositionRole);
            editor.TogglePositionSlotCommand.Execute(step); Assert.Null(step.CellPositionRole);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public void ToggleOverwriteDuplicateShots_RecompilesPlanPreviewAndPersists() => PlanSta.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-editor-overwrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db); var repo = new FileRecipeRepository(root);
            var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
            TemplatePlanTestData.Entries(plan).Add(new() { Key = "again", TemplateRecipeId = source.Id, Cameras = new() { TemplatePlanTestData.Cameras[0].Copy() } });
            await repo.SaveAsync(source); await repo.SaveAsync(plan);
            foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            using var editor = new RecipeEditorViewModel(repo, positions);
            var model = editor.Recipes.Single(r => r.Id == plan.Id); editor.SelectRecipeCommand.Execute(model);
            var planEditor = Assert.IsType<CapturePlanEditorViewModel>(editor.PlanEditor); await planEditor.LoadPositionsAsync();
            Assert.False(model.OverwriteDuplicateShots);
            IEnumerable<int> Offsets() => planEditor.GeneratedSteps.Where(g => g.Step.Kind == RecipeStepKind.CameraCommand && g.Step.CameraOperation == CameraControlOps.Capture).Select(g => g.Step.ShotNumberOffset);
            Assert.Contains(Offsets(), o => o > 0);
            model.OverwriteDuplicateShots = true;
            Assert.All(Offsets(), o => Assert.Equal(0, o));
            Assert.Contains(planEditor.GeneratedSteps, g => g.Label.Contains("시작번호 0"));
            editor.SaveRecipeCommand.Execute(null);
            Assert.True(Assert.IsType<Recipe>(await repo.GetByIdAsync(plan.Id)).OverwriteDuplicateShots);
            model.OverwriteDuplicateShots = false;
            Assert.Contains(Offsets(), o => o > 0);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public void RecipeTypeAndValidationFlagsNotify()
    {
        var model = new RecipeModel(); var changed = new List<string?>(); model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        model.Plan = new CapturePlan { Entries = new() }; model.PlanValidationError = "위치 누락";
        Assert.Contains(nameof(RecipeModel.IsPlanRecipe), changed); Assert.Contains(nameof(RecipeModel.TypeLabel), changed); Assert.Equal("실행 불가: 위치 누락", model.PlanValidationLabel);
    }
}
