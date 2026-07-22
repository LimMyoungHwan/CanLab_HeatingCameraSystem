using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Agent.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Protocols.Simulation;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class FakePlcControllerTests
    {
        [Fact]
        public async Task ConnectAsync_SetsIsConnected()
        {
            var plc = new FakePlcController();
            Assert.False(plc.IsConnected);

            await plc.ConnectAsync("any", 0);

            Assert.True(plc.IsConnected);
        }

        [Fact]
        public async Task SetTargetTemperature_SnapsCurrentToTarget()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            await plc.SetTargetTemperatureAsync(42.5f);
            var actual = await plc.GetCurrentTemperatureAsync();

            Assert.Equal(42.5f, actual);
        }

        [Fact]
        public async Task SetTargetHumidity_SnapsCurrentToTarget()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            await plc.SetTargetHumidityAsync(73.0f);
            var actual = await plc.GetCurrentHumidityAsync();

            Assert.Equal(73.0f, actual);
        }

        [Fact]
        public async Task ServoMove_ReportsArrivedImmediately()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            await plc.MoveServoToPositionAsync(7);
            var arrived = await plc.IsServoAtPositionAsync(7);

            Assert.True(arrived);
        }

        [Fact]
        public async Task MoveToCoordinate_SetsServoXYPosition()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            await plc.MoveToCoordinateAsync(1234, 5678);
            var s = await plc.ReadStatusAsync();

            Assert.Equal(1234, s.ServoXPosition);
            Assert.Equal(5678, s.ServoYPosition);
        }

        [Fact]
        public async Task SetBlackBodyTemperature_PerIndexState()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            await plc.SetBlackBodyTemperatureAsync(0, 35.0f);
            await plc.SetBlackBodyTemperatureAsync(1, 40.0f);

            Assert.Equal(35.0f, await plc.GetCurrentBlackBodyTemperatureAsync(0));
            Assert.Equal(40.0f, await plc.GetCurrentBlackBodyTemperatureAsync(1));
        }

        [Fact]
        public async Task CallBeforeConnect_Throws()
        {
            var plc = new FakePlcController();

            await Assert.ThrowsAsync<InvalidOperationException>(() => plc.StartChamberAsync());
        }
    }

    public class FakeSerialShutterControllerTests
    {
        [Fact]
        public async Task OpenClose_TracksStatePerCamera()
        {
            var s = new FakeSerialShutterController();
            await s.ConnectAsync();

            await s.OpenShutterAsync(1);
            await s.CloseShutterAsync(2);

            Assert.True(await s.GetShutterStateAsync(1));
            Assert.False(await s.GetShutterStateAsync(2));
        }

        [Fact]
        public async Task CallBeforeConnect_Throws()
        {
            var s = new FakeSerialShutterController();

            await Assert.ThrowsAsync<InvalidOperationException>(() => s.OpenShutterAsync(0));
        }

        [Fact]
        public async Task Disconnect_ResetsState()
        {
            var s = new FakeSerialShutterController();
            await s.ConnectAsync();
            Assert.True(s.IsConnected);

            s.Disconnect();

            Assert.False(s.IsConnected);
        }
    }

    public class FakeCameraCaptureServiceTests
    {
        [Fact]
        public void CaptureFrame_WritesValidJpegToStorage()
        {
            string dir = Path.Combine(Path.GetTempPath(), "HCS_SimTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var cam = new FakeCameraCaptureService(dir, "AgentTest");
                Assert.True(cam.InitializeCamera(0));

                bool ok = cam.CaptureFrame(out string path);

                Assert.True(ok);
                Assert.True(File.Exists(path));
                var bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Length > 1024);
                Assert.Equal(0xFF, bytes[0]);
                Assert.Equal(0xD8, bytes[1]);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }

    public class RecipeEngineWithFakePlcTests
    {
        [Fact]
        public async Task FullRecipeRun_WithFakePlc_CompletesAndWritesHistory()
        {
            var plc          = new FakePlcController();
            await plc.ConnectAsync("any");

            var mockNats     = new Mock<INatsCommunicationService>();
            var mockHistory  = new Mock<ICaptureHistoryRepository>();
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                       .Returns(Task.CompletedTask);

            Action<CaptureResultMessage>? resultCb = null;
            mockNats
                .Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(cb => resultCb = cb)
                .Returns(Task.CompletedTask);
            mockNats
                .Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                .Callback<CaptureCommandMessage>(cmd => resultCb?.Invoke(new CaptureResultMessage
                {
                    AgentId      = cmd.TargetAgentId,
                    RecipeStepId = cmd.RecipeStepId,
                    IsSuccess    = true,
                    ImagePath    = "/sim/img.jpg",
                    Timestamp    = DateTime.UtcNow
                }))
                .Returns(Task.CompletedTask);

            var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object);
            var recipe = new Recipe
            {
                Name                    = "SimRecipe",
                GlobalTargetTemperature = 30.0f,
                GlobalTargetHumidity    = 55.0f,
                Steps = new List<RecipeStep>
                {
                    new RecipeStep { CameraIndex = 0, TargetPositionIndex = 1, TargetBlackBodyTemperature = 35.0f },
                    new RecipeStep { CameraIndex = 1, TargetPositionIndex = 2, TargetBlackBodyTemperature = 40.0f }
                }
            };

            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Exactly(2));
            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_0")), Times.Once);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(
                It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_1")), Times.Once);

            Assert.Equal(30.0f, await plc.GetCurrentTemperatureAsync());
            Assert.Equal(40.0f, await plc.GetCurrentBlackBodyTemperatureAsync(0));
        }

        [Fact]
        public async Task RecipeRun_WithImageBytes_WritesCachedFile_AndHistoryPointsToIt()
        {
            string cacheDir = Path.Combine(Path.GetTempPath(), "HCS_CacheTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                var plc = new FakePlcController();
                await plc.ConnectAsync("any");

                var mockNats    = new Mock<INatsCommunicationService>();
                var mockHistory = new Mock<ICaptureHistoryRepository>();
                var stored      = new List<CaptureHistoryRecord>();
                mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                           .Callback<CaptureHistoryRecord>(r => stored.Add(r))
                           .Returns(Task.CompletedTask);

                byte[] fakeJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 0xFF, 0xD9 };
                Action<CaptureResultMessage>? resultCb = null;
                mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                        .Callback<Action<CaptureResultMessage>>(cb => resultCb = cb)
                        .Returns(Task.CompletedTask);
                mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                        .Callback<CaptureCommandMessage>(cmd => resultCb?.Invoke(new CaptureResultMessage
                        {
                            AgentId      = cmd.TargetAgentId,
                            RecipeStepId = cmd.RecipeStepId,
                            IsSuccess    = true,
                            ImagePath    = @"C:\agent\local\img.jpg",
                            ImageBytes   = fakeJpeg,
                            Timestamp    = DateTime.UtcNow
                        }))
                        .Returns(Task.CompletedTask);

                var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object, null, cacheDir);
                var recipe = new Recipe
                {
                    Name = "CacheTest",
                    GlobalTargetTemperature = 25.0f,
                    GlobalTargetHumidity    = 50.0f,
                    Steps = { new RecipeStep { CameraIndex = 0, TargetPositionIndex = 1, TargetBlackBodyTemperature = 30.0f } }
                };

                await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

                Assert.Single(stored);
                var rec = stored[0];
                Assert.StartsWith(cacheDir, rec.ImagePath);
                Assert.True(File.Exists(rec.ImagePath), $"cache file missing: {rec.ImagePath}");
                Assert.Equal(fakeJpeg, File.ReadAllBytes(rec.ImagePath));
            }
            finally
            {
                if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, recursive: true);
            }
        }

        [Fact]
        public async Task RecipeRun_WithoutImageBytes_FallsBackToAgentPath()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            CaptureHistoryRecord? stored = null;
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                       .Callback<CaptureHistoryRecord>(r => stored = r)
                       .Returns(Task.CompletedTask);

            Action<CaptureResultMessage>? resultCb = null;
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                    .Callback<Action<CaptureResultMessage>>(cb => resultCb = cb)
                    .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                    .Callback<CaptureCommandMessage>(cmd => resultCb?.Invoke(new CaptureResultMessage
                    {
                        AgentId      = cmd.TargetAgentId,
                        RecipeStepId = cmd.RecipeStepId,
                        IsSuccess    = true,
                        ImagePath    = "/agent/legacy/path.jpg",
                        ImageBytes   = null,
                        Timestamp    = DateTime.UtcNow
                    }))
                    .Returns(Task.CompletedTask);

            var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object, null, imageCacheDir: null);
            var recipe = new Recipe
            {
                Name = "FallbackTest",
                Steps = { new RecipeStep { CameraIndex = 0, TargetPositionIndex = 1, TargetBlackBodyTemperature = 30.0f } }
            };

            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal("/agent/legacy/path.jpg", stored!.ImagePath);
        }
    }
}
