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
            var recipe = HappyPathRecipe();

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

        // S2: humidity drifts out of the ChamberControl safety band, operator never resumes (gate throws)
        //     -> the later capture step is NEVER reached and an Error alarm is raised.
        [Fact]
        public async Task ExecuteRecipeAsync_WhenDriftAndNoResume_BlocksCaptureAndAlarms()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(90.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            WireCaptureRoundTrip(mockNats);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SafetyBandRecipe();

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
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync())
                   .Returns(() => Task.FromResult(resumed ? 50.0f : 90.0f));
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(mockNats);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = SafetyBandRecipe();

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

        // S4: chamber never reaches target temperature while stabilization is required -> engine stays in the
        //     temperature wait and never reaches the capture step (proven by cancellation, no capture).
        [Fact]
        public async Task ExecuteRecipeAsync_WhenChamberNeverStabilizes_WaitsBeforeCapture()
        {
            var mockPlc     = new Mock<IPlcController>();
            var mockNats    = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(20.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(40.0f);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);
            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.ChamberControl,
                        TargetChamberTemperature = 80,
                        TargetChamberHumidity = 40,
                        WaitForChamberStabilization = true
                    },
                    new RecipeStep { Kind = RecipeStepKind.CameraCommand, CameraOperation = CameraControlOps.Capture, CameraIndex = 1 }
                }
            };

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

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.ExecuteRecipeAsync(HappyPathRecipe()));

            mockPlc.Verify(p => p.StartChamberAsync(), Times.Never);
            mockPlc.Verify(p => p.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenEmergencyStopRaisedMidRun_StopsChamberAndAborts()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(40.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object);

            // 챔버가 켜진 직후 PLC 알람이 뜬 상황을 재현한다. 서보 이동 트리거를 훅으로 삼아
            // 그 시점에 비상정지를 걸면, 이후 스텝은 실행되지 않고 챔버는 반드시 정지해야 한다.
            mockPlc.Setup(p => p.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()))
                   .Callback(() => engine.RequestEmergencyStop())
                   .Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 40, TargetChamberHumidity = 50, WaitForChamberStabilization = false },
                    new() { Kind = RecipeStepKind.MotorMove, MotorMoveType = MotorMoveType.Manual, PositionX = 10, PositionY = 20 },
                    new() { Kind = RecipeStepKind.CameraCommand, CameraIndex = 1, CameraOperation = CameraControlOps.Capture }
                }
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => engine.ExecuteRecipeAsync(recipe).WaitAsync(TimeSpan.FromSeconds(5)));

            mockPlc.Verify(p => p.StartChamberAsync(), Times.Once);
            mockPlc.Verify(p => p.StopChamberAsync(), Times.Once);
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
        public async Task ExecuteRecipeAsync_ChamberControl_WhenNotWaiting_SkipsTemperatureStabilization()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            // 목표(80)와 영원히 다른 현재 온도 — 대기 로직이 살아 있으면 이 테스트는 끝나지 않는다.
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(20.0f);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new()
                    {
                        Kind = RecipeStepKind.ChamberControl,
                        TargetChamberTemperature = 80,
                        TargetChamberHumidity = 40,
                        WaitForChamberStabilization = false
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockPlc.Verify(p => p.SetTargetTemperatureAsync(80), Times.Once);
            mockPlc.Verify(p => p.SetControlTemperatureAsync(80), Times.Once);
            mockPlc.Verify(p => p.SetTargetHumidityAsync(40), Times.Once);
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

        [Fact]
        public async Task ExecuteRecipeAsync_StartsChamberWithChillerAndDoorLocked_AndWritesBothTargetAndControlTemperature()
        {
            var plc = new Mock<IPlcController>();
            var nats = new Mock<INatsCommunicationService>();
            var history = new Mock<ICaptureHistoryRepository>();
            plc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            plc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            plc.Setup(p => p.GetCurrentBlackBodyTemperatureAsync(It.IsAny<int>())).ReturnsAsync(30.0f);
            plc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot
            {
                Chiller = false, DoorLock = false, ServoXBusy = false, ServoYBusy = false
            });
            history.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(nats);

            await new RecipeEngine(plc.Object, nats.Object, history.Object).ExecuteRecipeAsync(HappyPathRecipe());

            plc.Verify(p => p.SetEquipmentAsync(PlcEquipment.Chiller, true), Times.Once);
            plc.Verify(p => p.SetEquipmentAsync(PlcEquipment.DoorLock, true), Times.Once);
            plc.Verify(p => p.SetTargetTemperatureAsync(25.0f), Times.AtLeastOnce);
            plc.Verify(p => p.SetControlTemperatureAsync(25.0f), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_ChamberControl_WhenTemperatureOutsideSafetyBand_RaisesErrorAndInvokesResume()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);

            bool resumeInvoked = false;
            // 목표(25℃)에서 크게 벗어난 60℃로 첫 판정을 이탈시키고, 재개 후 25℃로 회복시켜 안전 밴드 루프를 끝낸다.
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync())
                   .Returns(() => Task.FromResult(resumeInvoked ? 25.0f : 60.0f));

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.ChamberControl,
                        TargetChamberTemperature = 25,
                        TargetChamberHumidity = 50,
                        SafetyTempTolerance = 2.0f,
                        WaitForChamberStabilization = false
                    }
                }
            };

            Func<CancellationToken, Task> resume = ct =>
            {
                resumeInvoked = true;
                return Task.CompletedTask;
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe, CancellationToken.None, null, resume)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(resumeInvoked);
            Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_ChamberControl_WritesHumidityBeforeTemperature()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            var order = new List<string>();
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.SetTargetHumidityAsync(It.IsAny<float>()))
                   .Callback(() => order.Add("humidity"))
                   .Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.SetTargetTemperatureAsync(It.IsAny<float>()))
                   .Callback(() => order.Add("temperature"))
                   .Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.ChamberControl,
                        TargetChamberTemperature = 30,
                        TargetChamberHumidity = 60,
                        WaitForChamberStabilization = false
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { "humidity", "temperature" }, order);
        }

        private static Recipe HappyPathRecipe() => new Recipe
        {
            Name = "Test Recipe",
            Steps = new List<RecipeStep>
            {
                new RecipeStep { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 25.0, TargetChamberHumidity = 50.0 },
                new RecipeStep { Kind = RecipeStepKind.MotorMove, MotorMoveType = MotorMoveType.Manual, PositionX = 100, PositionY = 200 },
                new RecipeStep { Kind = RecipeStepKind.CameraCommand, CameraOperation = CameraControlOps.Capture, CameraIndex = 1 }
            }
        };

        private static Recipe SafetyBandRecipe() => new Recipe
        {
            Name = "Safety Recipe",
            Steps = new List<RecipeStep>
            {
                new RecipeStep
                {
                    Kind = RecipeStepKind.ChamberControl,
                    TargetChamberTemperature = 25.0,
                    TargetChamberHumidity = 50.0,
                    SafetyHumidityTolerance = 5.0f
                },
                new RecipeStep { Kind = RecipeStepKind.CameraCommand, CameraOperation = CameraControlOps.Capture, CameraIndex = 1 }
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
