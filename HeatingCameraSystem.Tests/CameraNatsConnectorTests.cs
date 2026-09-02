using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
                Assert.Equal("Camera 0", published.Alias);
                Assert.Equal(0, published.CameraIndex);
                Assert.Equal(CaptureSource.Recipe, published.Source);
                Assert.False(string.IsNullOrWhiteSpace(published.CaptureId));
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
                double handledBiasTarget = 0;

                await using (var connector = new CameraNatsConnector(
                    natsMock.Object,
                    manager,
                    store,
                    new[] { descriptor },
                    cameraControlHandler: (camera, message) =>
                    {
                        handledCamera = camera;
                        handledOp = message.Op;
                        handledBiasTarget = message.BiasTargetLevel;
                        return Task.FromResult((Success: true, Message: "ok"));
                    }))
                {
                    await connector.HandleCameraControlAsync(descriptor, new CameraControlMessage
                    {
                        AgentId = descriptor.AgentId,
                        CameraIndex = descriptor.OpenCvIndex,
                        Op = CameraControlOps.Run,
                        RequestId = "request-1",
                        BiasTargetLevel = 8200,
                        Timestamp = DateTime.UtcNow
                    });
                }

                Assert.Same(descriptor, handledCamera);
                Assert.Equal(CameraControlOps.Run, handledOp);
                Assert.Equal(8200, handledBiasTarget);
                Assert.NotNull(published);
                Assert.Equal(descriptor.AgentId, published!.AgentId);
                Assert.Equal(CameraControlOps.Run, published.Op);
                Assert.Equal("request-1", published.RequestId);
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

        [Fact]
        public async Task SyncSubscriptions_AddsOnlyNewCameraSubscriptions()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_sync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                var cameras = new List<CameraDescriptor>
                {
                    new("cam0", 0, "Camera 0")
                };
                var subscribed = new List<string>();
                natsMock.Setup(n => n.ConnectAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
                natsMock.Setup(n => n.SubscribeCaptureCommandAsync(It.IsAny<string>(), It.IsAny<Action<CaptureCommandMessage>>()))
                    .Callback<string, Action<CaptureCommandMessage>>((agentId, _) => subscribed.Add($"capture:{agentId}"))
                    .Returns(Task.CompletedTask);
                natsMock.Setup(n => n.SubscribeCameraControlAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlMessage>>()))
                    .Callback<string, Action<CameraControlMessage>>((agentId, _) => subscribed.Add($"control:{agentId}"))
                    .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(natsMock.Object, manager, store, cameras);

                connector.Start("nats://127.0.0.1:4222");
                await WaitUntilAsync(() => subscribed.Count == 2);

                cameras.Add(new CameraDescriptor("cam1", 1, "Camera 1"));
                await connector.SyncSubscriptionsAsync();
                await connector.SyncSubscriptionsAsync();

                Assert.Equal(4, subscribed.Count);
                Assert.Single(subscribed.FindAll(s => s == "capture:cam1"));
                Assert.Single(subscribed.FindAll(s => s == "control:cam1"));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++)
            {
                await Task.Delay(10);
            }

            Assert.True(condition());
        }
    }
}
