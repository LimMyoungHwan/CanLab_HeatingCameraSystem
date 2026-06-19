using System;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class NatsCommunicationServiceTests
    {
        [Fact]
        public async Task PublishCaptureCommand_ShouldSucceed_Mock()
        {
            // Arrange
            var mockService = new Mock<INatsCommunicationService>();
            var command = new CaptureCommandMessage
            {
                TargetAgentId = "Agent01",
                RecipeStepId = "Step1",
                Timestamp = DateTime.UtcNow
            };

            mockService.Setup(s => s.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                       .Returns(Task.CompletedTask)
                       .Verifiable();

            // Act
            await mockService.Object.PublishCaptureCommandAsync(command);

            // Assert
            mockService.Verify(s => s.PublishCaptureCommandAsync(command), Times.Once);
        }

        [Fact]
        public async Task SubscribeAgentStatus_ShouldTriggerCallback_Mock()
        {
            // Arrange
            var mockService = new Mock<INatsCommunicationService>();
            AgentStatusMessage? receivedMessage = null;
            var testMessage = new AgentStatusMessage { AgentId = "Agent01", CameraStatus = CameraStatus.Connected };

            mockService.Setup(s => s.SubscribeAgentStatusAsync(It.IsAny<Action<AgentStatusMessage>>()))
                .Callback<Action<AgentStatusMessage>>(action => action(testMessage))
                .Returns(Task.CompletedTask);

            // Act
            await mockService.Object.SubscribeAgentStatusAsync(msg => receivedMessage = msg);

            // Assert
            Assert.NotNull(receivedMessage);
            Assert.Equal("Agent01", receivedMessage.AgentId);
            Assert.Equal(CameraStatus.Connected, receivedMessage.CameraStatus);
        }
    }
}
