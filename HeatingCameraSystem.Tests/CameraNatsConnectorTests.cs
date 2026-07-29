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

        [Fact]
        public async Task HandleCameraControl_RoutesToHandler_AndPublishesAck()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_control_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CameraControlAckMessage? published = null;
                natsMock.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                        .Callback<CameraControlAckMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0");
                CameraDescriptor? handledCamera = null;
                string? handledOp = null;

                await using (var connector = new CameraNatsConnector(
                    natsMock.Object,
                    manager,
                    store,
                    new[] { descriptor },
                    cameraControlHandler: (camera, op) =>
                    {
                        handledCamera = camera;
                        handledOp = op;
                        return Task.FromResult((Success: true, Message: "ok"));
                    }))
                {
                    await connector.HandleCameraControlAsync(descriptor, new CameraControlMessage
                    {
                        AgentId = descriptor.AgentId,
                        CameraIndex = descriptor.OpenCvIndex,
                        Op = CameraControlOps.Run,
                        Timestamp = DateTime.UtcNow
                    });
                }

                Assert.Same(descriptor, handledCamera);
                Assert.Equal(CameraControlOps.Run, handledOp);
                Assert.NotNull(published);
                Assert.Equal(descriptor.AgentId, published!.AgentId);
                Assert.Equal(CameraControlOps.Run, published.Op);
                Assert.True(published.IsSuccess);
                Assert.Equal("ok", published.Message);
                natsMock.Verify(n => n.PublishCameraControlAckAsync(It.Is<CameraControlAckMessage>(m =>
                    m.AgentId == descriptor.AgentId &&
                    m.Op == CameraControlOps.Run &&
                    m.IsSuccess)), Times.Once);

                published = null;
                await using (var connectorWithoutHandler = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor }))
                {
                    await connectorWithoutHandler.HandleCameraControlAsync(descriptor, new CameraControlMessage
                    {
                        AgentId = descriptor.AgentId,
                        CameraIndex = descriptor.OpenCvIndex,
                        Op = CameraControlOps.Stop,
                        Timestamp = DateTime.UtcNow
                    });
                }

                Assert.NotNull(published);
                Assert.False(published!.IsSuccess);
                Assert.Equal("control handler not wired", published.Message);
                natsMock.Verify(n => n.PublishCameraControlAckAsync(It.Is<CameraControlAckMessage>(m =>
                    m.AgentId == descriptor.AgentId &&
                    m.Op == CameraControlOps.Stop &&
                    !m.IsSuccess)), Times.Once);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
