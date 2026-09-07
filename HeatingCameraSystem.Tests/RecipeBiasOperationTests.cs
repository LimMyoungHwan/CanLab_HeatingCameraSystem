using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeBiasOperationTests
    {
        private static RecipeStep BiasStep(string op) => new()
        {
            Kind = RecipeStepKind.CameraCommand,
            CameraOperation = op
        };

        [Theory]
        [InlineData(ChamberRange.Low, CameraControlOps.BiasLow)]
        [InlineData(ChamberRange.Mid, CameraControlOps.BiasMid)]
        [InlineData(ChamberRange.High, CameraControlOps.BiasHigh)]
        public void ResolveCameraOperation_TargetChamberOverridesStoredOp(ChamberRange range, string expected)
        {
            var target = new RecipeCameraTarget { TargetChamber = range };

            Assert.Equal(expected, RecipeEngine.ResolveCameraOperation(BiasStep(CameraControlOps.BiasMid), target));
        }

        [Fact]
        public void ResolveCameraOperation_NoTargetChamber_KeepsStoredOp()
        {
            var target = new RecipeCameraTarget();

            Assert.Equal(CameraControlOps.BiasHigh, RecipeEngine.ResolveCameraOperation(BiasStep(CameraControlOps.BiasHigh), target));
        }

        [Fact]
        public void ResolveCameraOperation_NonBiasOp_Untouched()
        {
            var target = new RecipeCameraTarget { TargetChamber = ChamberRange.Low };

            Assert.Equal(CameraControlOps.Capture, RecipeEngine.ResolveCameraOperation(BiasStep(CameraControlOps.Capture), target));
        }
    }
}
