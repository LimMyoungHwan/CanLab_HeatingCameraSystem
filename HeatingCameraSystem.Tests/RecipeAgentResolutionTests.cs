using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeAgentResolutionTests
    {
        [Fact]
        public async Task CameraIndexResolvesToAgent()
        {
            var (engine, mockNats) = CreateEngine();
            var recipe = CreateRecipe(cameraIndex: 7);

            await engine.ExecuteRecipeAsync(recipe);

            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_7")), Times.Once);
        }

        [Fact]
        public async Task CameraAliasResolvesToAgent()
        {
            var mockDeviceRepo = new Mock<ICameraDeviceRepository>();
            mockDeviceRepo.Setup(r => r.GetByAliasAsync("CAM-X"))
                          .ReturnsAsync(new CameraDevice { Alias = "CAM-X", AgentId = "Agent_X" });
            var (engine, mockNats) = CreateEngine(mockDeviceRepo.Object);
            var recipe = CreateRecipe(cameraIndex: 7, cameraAlias: "CAM-X");

            await engine.ExecuteRecipeAsync(recipe);

            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_X")), Times.Once);
        }

        [Fact]
        public async Task LiveDirectoryAliasWinsOverDeviceRepo()
        {
            var mockDeviceRepo = new Mock<ICameraDeviceRepository>();
            mockDeviceRepo.Setup(r => r.GetByAliasAsync("CAM-X"))
                          .ReturnsAsync(new CameraDevice { Alias = "CAM-X", AgentId = "Stale_Agent" });

            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { Alias = "CAM-X", AgentId = "PC1_Agent_3" });

            var (engine, mockNats) = CreateEngine(mockDeviceRepo.Object, directory);
            var recipe = CreateRecipe(cameraIndex: 7, cameraAlias: "CAM-X");

            await engine.ExecuteRecipeAsync(recipe);

            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "PC1_Agent_3")), Times.Once);
        }

        [Fact]
        public async Task LiveDirectoryMissFallsBackToDeviceRepo()
        {
            var mockDeviceRepo = new Mock<ICameraDeviceRepository>();
            mockDeviceRepo.Setup(r => r.GetByAliasAsync("CAM-X"))
                          .ReturnsAsync(new CameraDevice { Alias = "CAM-X", AgentId = "Agent_X" });

            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { Alias = "OTHER", AgentId = "PC1_Agent_9" });

            var (engine, mockNats) = CreateEngine(mockDeviceRepo.Object, directory);
            var recipe = CreateRecipe(cameraIndex: 7, cameraAlias: "CAM-X");

            await engine.ExecuteRecipeAsync(recipe);

            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_X")), Times.Once);
        }

        private static Recipe CreateRecipe(int cameraIndex, string? cameraAlias = null) => new()
        {
            GlobalTargetTemperature = 25.0f,
            Steps = new List<RecipeStep>
            {
                new RecipeStep
                {
                    CameraIndex = cameraIndex,
                    CameraAlias = cameraAlias,
                    TargetPositionIndex = 1,
                    TargetBlackBodyTemperature = 30.0f
                }
            }
        };

        private static (RecipeEngine Engine, Mock<INatsCommunicationService> Nats) CreateEngine(
            ICameraDeviceRepository? deviceRepo = null,
            AgentDirectory? directory = null)
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot());
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);

            Action<CaptureResultMessage>? resultCb = null;
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                    .Callback<Action<CaptureResultMessage>>(cb => resultCb = cb)
                    .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                    .Callback<CaptureCommandMessage>(cmd => resultCb?.Invoke(new CaptureResultMessage
                    {
                        AgentId = cmd.TargetAgentId,
                        RecipeStepId = cmd.RecipeStepId,
                        IsSuccess = false,
                        Timestamp = DateTime.UtcNow
                    }))
                    .Returns(Task.CompletedTask);

            var engine = new RecipeEngine(
                mockPlc.Object,
                mockNats.Object,
                mockHistory.Object,
                deviceRepo: deviceRepo,
                agentDirectory: directory);
            return (engine, mockNats);
        }
    }
}
