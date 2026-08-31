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
        // S1: chamber at target, no drift -> every step captures, no pause, no error alarm.
        [Fact]
        public async Task ExecuteRecipeAsync_ShouldRunAllStepsAndCallPlc()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(mockNats);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SingleStepRecipe();

            AlarmSink.Entries.Clear();
            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None);

            mockPlc.Verify(p => p.StartChamberAsync(), Times.Once);
            mockPlc.Verify(p => p.SetTargetTemperatureAsync(25.0f), Times.Once);
            mockPlc.Verify(p => p.MoveToCoordinateAsync(100, 200), Times.Once);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.Is<CaptureCommandMessage>(m => m.TargetAgentId == "Agent_1")), Times.Once);
            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Once);
            mockPlc.Verify(p => p.StopChamberAsync(), Times.Once);
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        // S2: humidity drifts out of band right before capture, operator never resumes (gate cancels)
        //     -> capture command is NEVER published and an Error alarm is raised.
        [Fact]
        public async Task ExecuteRecipeAsync_WhenDriftAndNoResume_BlocksCaptureAndAlarms()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            int humCall = 0;
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).Returns(() => Task.FromResult(humCall++ == 0 ? 50.0f : 90.0f));
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);
            WireCaptureRoundTrip(mockNats);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SingleStepRecipe();

            bool gateInvoked = false;
            Func<CancellationToken, Task> neverResume = ct =>
            {
                gateInvoked = true;
                throw new OperationCanceledException(ct);
            };

            AlarmSink.Entries.Clear();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => engine.ExecuteRecipeAsync(recipe, CancellationToken.None, null, neverResume));

            Assert.True(gateInvoked);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
            Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        // S3: humidity drifts out of band, then operator resumes after chamber recovers -> capture proceeds.
        [Fact]
        public async Task ExecuteRecipeAsync_WhenDriftThenResume_ProceedsAfterRecovery()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            bool resumed = false;
            int humCall = 0;
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync())
                   .Returns(() => Task.FromResult(resumed ? 50.0f : (humCall++ == 0 ? 50.0f : 90.0f)));
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(mockNats);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SingleStepRecipe();

            int gateCalls = 0;
            Func<CancellationToken, Task> resume = ct =>
            {
                gateCalls++;
                resumed = true;
                return Task.CompletedTask;
            };

            AlarmSink.Entries.Clear();
            await engine.ExecuteRecipeAsync(recipe, CancellationToken.None, null, resume);

            Assert.Equal(1, gateCalls);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Once);
            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Once);
            Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        // S4: at startup temperature is reached but humidity is out of band -> engine stays in the initial
        //     stabilization wait and never enters the step loop (proven by cancellation, no capture).
        [Fact]
        public async Task ExecuteRecipeAsync_WhenInitialHumidityOutOfBand_WaitsBeforeSteps()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(90.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SingleStepRecipe();

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(500);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => engine.ExecuteRecipeAsync(recipe, cts.Token));

            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenEmergencyStopRequested_DoesNotStartOrAdvanceRecipe()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);

            engine.RequestEmergencyStop();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteRecipeAsync(SingleStepRecipe()));

            mockPlc.Verify(p => p.StartChamberAsync(), Times.Never);
            mockPlc.Verify(p => p.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_SegmentedSteps_RunOnlyTheirAssignedOperation()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var ackCallbacks = new Dictionary<string, Action<CameraControlAckMessage>>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(30.0f);
            mockPlc.Setup(p => p.MoveServoToPositionAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockNats.Setup(n => n.SubscribeCameraControlAckAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlAckMessage>>()))
                .Callback<string, Action<CameraControlAckMessage>>((agentId, callback) => ackCallbacks[agentId] = callback)
                .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
                .Callback<CameraControlMessage>(message => ackCallbacks[message.AgentId](new CameraControlAckMessage
                {
                    AgentId = message.AgentId,
                    CameraIndex = message.CameraIndex,
                    Op = message.Op,
                    RequestId = message.RequestId,
                    IsSuccess = true
                }))
                .Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.MotorMove, PositionX = 10, PositionY = 20 },
                    new() { Kind = RecipeStepKind.MotorMove, MotorMoveType = MotorMoveType.Automatic, TargetPositionIndex = 7 },
                    new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 30, TargetChamberHumidity = 60 },
                    new()
                    {
                        Kind = RecipeStepKind.CameraCommand,
                        CameraOperation = CameraControlOps.Nuc,
                        CameraTargets = new List<RecipeCameraTarget>
                        {
                            new() { AgentId = "Agent_1", CameraIndex = 1 },
                            new() { AgentId = "Agent_2", CameraIndex = 2 }
                        }
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object).ExecuteRecipeAsync(recipe);

            mockPlc.Verify(p => p.MoveToCoordinateAsync(10, 20), Times.Once);
            mockPlc.Verify(p => p.MoveServoToPositionAsync(7), Times.Once);
            mockPlc.Verify(p => p.SetTargetTemperatureAsync(30), Times.Once);
            mockPlc.Verify(p => p.SetTargetHumidityAsync(60), Times.Once);
            mockNats.Verify(n => n.PublishCameraControlAsync(It.Is<CameraControlMessage>(m =>
                m.AgentId == "Agent_1" && m.Op == CameraControlOps.Nuc && !string.IsNullOrEmpty(m.RequestId))), Times.Once);
            mockNats.Verify(n => n.PublishCameraControlAsync(It.Is<CameraControlMessage>(m =>
                m.AgentId == "Agent_2" && m.Op == CameraControlOps.Nuc && !string.IsNullOrEmpty(m.RequestId))), Times.Once);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
            mockPlc.Verify(p => p.StopChamberAsync(), Times.Once);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_SegmentedSteps_BlackBodyControl_SetsTemperatureAndWaits()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var mockBlackBody = new Mock<IBlackBodyController>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockBlackBody.Setup(b => b.SetTemperatureAsync(It.IsAny<int>(), It.IsAny<float>())).Returns(Task.CompletedTask);
            mockBlackBody.Setup(b => b.GetCurrentTemperatureAsync(0)).ReturnsAsync(50.0f);
            mockBlackBody.Setup(b => b.GetCurrentTemperatureAsync(1)).ReturnsAsync(51.0f);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.BlackBodyControl, TargetBlackBodyTemperature = 50.0f, TargetBlackBodyTemperature1 = 51.0f, WaitForStabilization = true }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, blackBody: mockBlackBody.Object).ExecuteRecipeAsync(recipe);

            mockBlackBody.Verify(b => b.SetTemperatureAsync(0, 50.0f), Times.Once);
            mockBlackBody.Verify(b => b.SetTemperatureAsync(1, 51.0f), Times.Once);
            mockBlackBody.Verify(b => b.GetCurrentTemperatureAsync(0), Times.AtLeastOnce);
            mockBlackBody.Verify(b => b.GetCurrentTemperatureAsync(1), Times.AtLeastOnce);
        }

        private static Recipe SingleStepRecipe() => new Recipe
        {
            Name = "Test Recipe",
            GlobalTargetTemperature = 25.0f,
            GlobalTargetHumidity = 50.0f,
            Steps = new List<RecipeStep>
            {
                new RecipeStep { CameraIndex = 1, TargetPositionIndex = 5, PositionX = 100, PositionY = 200, TargetBlackBodyTemperature = 30.0f }
            }
        };

        private static void WireCaptureRoundTrip(Mock<INatsCommunicationService> mockNats)
        {
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
        }
    }
}
