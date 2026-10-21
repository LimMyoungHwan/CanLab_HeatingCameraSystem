using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CapturePlanConverterTests
{
    [Fact]
    public void CreateFromRecipeLinksWithoutGuessingSlotsOrEditingOriginal()
    {
        var source = TemplatePlanTestData.Template(); source.Steps.ForEach(s => { s.UseCellCamera = false; s.CellPositionRole = null; });
        string before = JsonSerializer.Serialize(source);
        var plan = CapturePlanConverter.CreateFromTemplate(source);
        Assert.NotEqual(source.Id, plan.Id); Assert.Empty(plan.Steps);
        var entry = Assert.Single(Assert.IsType<CapturePlan>(plan.Plan).Entries!);
        Assert.Equal(source.Id, entry.TemplateRecipeId); Assert.Empty(entry.Cameras);
        Assert.Equal(before, JsonSerializer.Serialize(source)); Assert.NotNull(plan.PlanValidationError);
    }
    [Fact]
    public void PlanCannotBeUsedAsNestedTemplate()
    {
        var source = TemplatePlanTestData.Template();
        Assert.Throws<ArgumentException>(() => CapturePlanConverter.CreateFromTemplate(TemplatePlanTestData.Plan(source)));
    }
    [Fact]
    public void FlattenUsesResolvedTemplatesAndClearsSlotMetadata()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var flattened = CapturePlanRecipeCopies.ToLegacyCopy(plan, TemplatePlanTestData.Positions(), new[] { source });
        Assert.Null(flattened.Plan); Assert.Equal(20, flattened.Steps.Count);
        Assert.All(flattened.Steps, s => { Assert.Null(s.PlanCellKey); Assert.False(s.UseCellCamera); Assert.Null(s.CellPositionRole); });
        Assert.Equal(TemplatePlanTestData.Cameras[1].AgentId, flattened.Steps[15].CameraTargets[0].AgentId);
    }
}
