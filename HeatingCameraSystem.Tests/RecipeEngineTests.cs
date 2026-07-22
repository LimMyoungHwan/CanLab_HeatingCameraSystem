using System;
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
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);

            Action<CaptureResultMessage>? resultCb = null;
            mockNats
                .Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(cb => resultCb = cb)
                .Returns(Task.CompletedTask);

            mockNats
                .Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                .Callback<CaptureCommandMessage>(cmd => resultCb?.Invoke(new CaptureResultMessage
                {
                    AgentId      = "Agent_1",
                    RecipeStepId = cmd.RecipeStepId,
                    IsSuccess    = true,
                    ImagePath    = "/test/image.jpg",
                    Timestamp    = DateTime.UtcNow
                }))
                .Returns(Task.CompletedTask);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);

            var recipe = new Recipe
            {
                Name = "Test Recipe",
                GlobalTargetTemperature = 25.0f,
                GlobalTargetHumidity = 50.0f,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { CameraIndex = 1, TargetPositionIndex = 5, PositionX = 100, PositionY = 200, TargetBlackBodyTemperature = 30.0f }
                }
            };

            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

            mockPlc.Verify(p => p.StartChamberAsync(), Times.Once);
            mockPlc.Verify(p => p.SetTargetTemperatureAsync(25.0f), Times.Once);
            mockPlc.Verify(p => p.MoveToCoordinateAsync(100, 200), Times.Once);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_1")), Times.Once);
            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Once);
            mockPlc.Verify(p => p.StopChamberAsync(), Times.Once);
        }
    }
}
