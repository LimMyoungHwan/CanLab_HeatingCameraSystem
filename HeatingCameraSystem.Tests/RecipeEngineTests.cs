using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeEngineTests
    {
        [Fact]
        public async Task ExecuteRecipeAsync_ShouldRunAllStepsAndCallPlc()
        {
            // Arrange
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();

            // Setup PLC to return true/expected values instantly
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.IsServoAtPositionAsync()).ReturnsAsync(true);
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object);

            var recipe = new Recipe
            {
                Name = "Test Recipe",
                GlobalTargetTemperature = 25.0f,
                GlobalTargetHumidity = 50.0f,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { CameraIndex = 1, TargetPositionIndex = 5, TargetBlackBodyTemperature = 30.0f }
                }
            };

            // Act
            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

            // Assert
            // Verify chamber start
            mockPlc.Verify(p => p.StartChamberAsync(), Times.Once);
            mockPlc.Verify(p => p.SetTargetTemperatureAsync(25.0f), Times.Once);

            // Verify servo movement
            mockPlc.Verify(p => p.MoveServoToPositionAsync(5), Times.Once);

            // Verify NATS capture command sent
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_1")), Times.Once);

            // Verify chamber stop
            mockPlc.Verify(p => p.StopChamberAsync(), Times.Once);
        }
    }
}
