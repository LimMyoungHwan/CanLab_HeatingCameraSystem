using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Tests;

public class RecipeCopyTests
{
    [Fact]
    public void DeepCopyIndependence()
    {
        var source = new Recipe
        {
            Name = "원본",
            GlobalTargetTemperature = 42.5f,
            GlobalTargetHumidity = 67.5f,
            TemperatureRampMinutes = 15,
            Steps = Enumerable.Range(1, 3).Select(i => new RecipeStep
            {
                StepId = $"step-{i}",
                CameraIndex = i,
                CameraAlias = $"CAM-{i:D2}",
                TargetPositionIndex = i + 10,
                TargetBlackBodyTemperature = 20 + i,
                PositionX = i * 100,
                PositionY = i * 200,
                TargetChamberTemperature = 30 + i,
                TargetChamberHumidity = 40 + i
            }).ToList(),
            Mappings =
            [
                new CameraMappingConfig { SlotId = "P01", CameraId = "CAM-01" },
                new CameraMappingConfig { SlotId = "P02", CameraId = "CAM-02" }
            ]
        };

        Recipe clone = RecipeEditorViewModel.CloneRecipe(source);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("원본 (복사)", clone.Name);
        Assert.Equal(3, clone.Steps.Count);
        Assert.Equal(2, clone.Mappings.Count);
        Assert.All(source.Steps.Zip(clone.Steps), pair =>
        {
            Assert.NotSame(pair.First, pair.Second);
            Assert.Equal(pair.First.StepId, pair.Second.StepId);
            Assert.Equal(pair.First.CameraIndex, pair.Second.CameraIndex);
            Assert.Equal(pair.First.CameraAlias, pair.Second.CameraAlias);
            Assert.Equal(pair.First.TargetPositionIndex, pair.Second.TargetPositionIndex);
            Assert.Equal(pair.First.TargetBlackBodyTemperature, pair.Second.TargetBlackBodyTemperature);
            Assert.Equal(pair.First.PositionX, pair.Second.PositionX);
            Assert.Equal(pair.First.PositionY, pair.Second.PositionY);
            Assert.Equal(pair.First.TargetChamberTemperature, pair.Second.TargetChamberTemperature);
            Assert.Equal(pair.First.TargetChamberHumidity, pair.Second.TargetChamberHumidity);
        });
        Assert.All(source.Mappings.Zip(clone.Mappings), pair =>
        {
            Assert.NotSame(pair.First, pair.Second);
            Assert.Equal(pair.First.SlotId, pair.Second.SlotId);
            Assert.Equal(pair.First.CameraId, pair.Second.CameraId);
        });

        clone.Steps[0].CameraIndex = 64;
        clone.Mappings[0].CameraId = "CAM-64";

        Assert.Equal(1, source.Steps[0].CameraIndex);
        Assert.Equal("CAM-01", source.Mappings[0].CameraId);
    }

    [Fact]
    public void EmptyRecipeCopy()
    {
        var source = new Recipe { Name = "빈 레시피" };

        Recipe clone = RecipeEditorViewModel.CloneRecipe(source);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal("빈 레시피 (복사)", clone.Name);
        Assert.Empty(clone.Steps);
        Assert.Empty(clone.Mappings);
    }
}
