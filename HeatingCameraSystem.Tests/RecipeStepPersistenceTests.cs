using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeStepPersistenceTests
    {
        [Fact]
        public async Task RecipeStep_PerPositionFields_RoundTripThroughLiteDb()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbRecipeRepository(db);

            var recipe = new Recipe { Name = "PerPositionRoundTrip" };
            recipe.Steps.Add(new RecipeStep
            {
                PositionX                 = 1234,
                PositionY                 = 5678,
                TargetChamberTemperature  = 25.5,
                TargetChamberHumidity     = 40.0,
                Kind = RecipeStepKind.BlackBodyControl,
                TargetBlackBodyTemperature = 37.5f,
                BlackBodyIndex = 1,
                WaitForStabilization = false,
                CameraOperation = CameraControlOps.Nuc,
                CameraTargets = new List<RecipeCameraTarget>
                {
                    new() { AgentId = "Agent_1", CameraIndex = 1 },
                    new() { AgentId = "Agent_2", CameraIndex = 2 }
                }
            });

            await repo.SaveAsync(recipe);
            var reloaded = await repo.GetByIdAsync(recipe.Id);

            Assert.NotNull(reloaded);
            var step = reloaded!.Steps.Single();
            Assert.Equal(1234, step.PositionX);
            Assert.Equal(5678, step.PositionY);
            Assert.Equal(25.5, step.TargetChamberTemperature);
            Assert.Equal(40.0, step.TargetChamberHumidity);
            Assert.Equal(RecipeStepKind.BlackBodyControl, step.Kind);
            Assert.Equal(37.5f, step.TargetBlackBodyTemperature);
            Assert.Equal(1, step.BlackBodyIndex);
            Assert.False(step.WaitForStabilization);
            Assert.Equal(CameraControlOps.Nuc, step.CameraOperation);
            Assert.Equal(new[] { "Agent_1", "Agent_2" }, step.CameraTargets.Select(target => target.AgentId));
        }

        [Fact]
        public void RecipeStepModel_SelectedCameraSummary_TracksCheckedTargets()
        {
            var step = new RecipeStepModel();
            step.CameraTargets.Add(new CameraTargetModel { AgentId = "Agent_1", CameraIndex = 1, IsSelected = true });
            step.CameraTargets.Add(new CameraTargetModel { AgentId = "Agent_2", CameraIndex = 2 });

            Assert.Equal("Agent_1 (CAM-01)", step.SelectedCameraSummary);

            step.CameraTargets[1].IsSelected = true;

            Assert.Equal("Agent_1 (CAM-01), Agent_2 (CAM-02)", step.SelectedCameraSummary);
        }
    }
}
