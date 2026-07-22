using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
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
                TargetChamberHumidity     = 40.0
            });

            await repo.SaveAsync(recipe);
            var reloaded = await repo.GetByIdAsync(recipe.Id);

            Assert.NotNull(reloaded);
            var step = reloaded!.Steps.Single();
            Assert.Equal(1234, step.PositionX);
            Assert.Equal(5678, step.PositionY);
            Assert.Equal(25.5, step.TargetChamberTemperature);
            Assert.Equal(40.0, step.TargetChamberHumidity);
        }
    }
}
