using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace HeatingCameraSystem.Tests;

public class CapturePlanPersistenceTests
{
    [Fact]
    public async Task FileAndLiteDbRoundTripLiveLinksSlotsAndCameraZero()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hcs-plan-persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
            using var db = new LiteDatabase(new MemoryStream());
            foreach (var repo in new HeatingCameraSystem.Core.Interfaces.IRecipeRepository[] { new FileRecipeRepository(folder), new LiteDbRecipeRepository(db) })
            {
                await repo.SaveAsync(source); await repo.SaveAsync(plan);
                var loaded = Assert.IsType<Recipe>(await repo.GetByIdAsync(source.Id));
                Assert.True(loaded.Steps[5].UseCellCamera); Assert.Equal(BlackBodyRole.Cold, loaded.Steps[2].CellPositionRole);
                var linked = Assert.IsType<Recipe>(await repo.GetByIdAsync(plan.Id));
                var entry = Assert.Single(TemplatePlanTestData.Entries(linked)); Assert.Equal(source.Id, entry.TemplateRecipeId); Assert.Equal(0, entry.Cameras[0].CameraIndex);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAsync(source.Id)); Assert.Contains(plan.Name, error.Message);
                await repo.DeleteAsync(plan.Id); await repo.DeleteAsync(source.Id); Assert.Null(await repo.GetByIdAsync(source.Id));
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void CsvPreservesSlotColumnsAndPlanJsonWithEscapedAgentNames()
    {
        var source = TemplatePlanTestData.Template(); source.Steps[2].CellPositionRole = BlackBodyRole.Room;
        var round = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(source));
        Assert.True(round.Steps[5].UseCellCamera); Assert.Equal(BlackBodyRole.Room, round.Steps[2].CellPositionRole);
        var plan = TemplatePlanTestData.Plan(source); TemplatePlanTestData.Entries(plan)[0].Cameras[0].AgentId = "Agent,\"quoted\"\nremote";
        var back = RecipeCsvSerializer.Deserialize(RecipeCsvSerializer.Serialize(plan));
        Assert.Equal(JsonSerializer.Serialize(plan.Plan), JsonSerializer.Serialize(back.Plan));
    }
    [Fact]
    public async Task OldPlanLoadClearsCachedExecutionAndShowsReadableReason()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hcs-old-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new FileRecipeRepository(folder);
            File.WriteAllText(Path.Combine(folder, "recipe", "old.json"), "{\"Id\":\"old\",\"Name\":\"old\",\"Plan\":{\"Conditions\":[]},\"Steps\":[{\"Kind\":2}]}");
            var old = Assert.IsType<Recipe>(await repo.GetByIdAsync("old"));
            Assert.Equal(CapturePlanCompiler.OldFormatMessage, old.PlanValidationError); Assert.Empty(old.Steps);
            Assert.Contains("Conditions", File.ReadAllText(Path.Combine(folder, "recipe", "old.json")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public void CloneIsIndependentButRetainsLiveRecipeReferenceAndStableEntryKey()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source); var copy = CapturePlanRecipeCopies.Clone(plan);
        var copied = TemplatePlanTestData.Entries(copy)[0]; var original = TemplatePlanTestData.Entries(plan)[0];
        copied.Cameras[0].AgentId = "changed";
        Assert.NotEqual(copied.Cameras[0].AgentId, original.Cameras[0].AgentId);
        Assert.Equal(original.TemplateRecipeId, copied.TemplateRecipeId); Assert.Equal(original.Key, copied.Key);
    }
}
