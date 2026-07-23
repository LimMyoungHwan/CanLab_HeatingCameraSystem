using System;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraNatsConnectorTests
    {
        [Fact]
        public async Task HandleCapture_TeesSnapshot_Persists_And_PublishesResult()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0");
                manager.Add(descriptor);
                await manager.StartAllAsync();

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor });

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    RecipeStepId = "s1",
                    Timestamp = DateTime.UtcNow
                });

                Assert.NotNull(published);
                Assert.True(published!.IsSuccess);
                Assert.Equal("cam0", published.AgentId);
                Assert.Equal("s1", published.RecipeStepId);
                Assert.NotNull(published.ImageBytes);
                Assert.True(published.ImageBytes!.Length > 0);
                Assert.True(File.Exists(published.ImagePath));

                Assert.Single(store.Query());

                await manager.StopAllAsync();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCapture_UnknownCamera_PublishesFailure()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                var descriptor = new CameraDescriptor("ghost", 7, "Ghost");
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor });

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "ghost",
                    RecipeStepId = "s1",
                    Timestamp = DateTime.UtcNow
                });

                Assert.NotNull(published);
                Assert.False(published!.IsSuccess);
                Assert.Equal("ghost", published.AgentId);
                Assert.Null(published.ImageBytes);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
