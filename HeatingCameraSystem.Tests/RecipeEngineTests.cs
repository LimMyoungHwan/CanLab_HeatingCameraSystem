using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
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
            Func<CancellationToken, Task<bool>> neverResume = ct =>
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
            Func<CancellationToken, Task<bool>> resume = ct =>
            {
                gateCalls++;
                resumed = true;
                return Task.FromResult(false);
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
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(60.0f);
            mockPlc.Setup(p => p.MoveServoToPositionAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false, CurrentPoint = 7 });
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
                    new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 30 },
                    new() { Kind = RecipeStepKind.HumidityControl, TargetChamberHumidity = 60 },
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
        public async Task ExecuteRecipeAsync_BlackBodyControl_UsesStepTolerance()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var mockBlackBody = new Mock<IBlackBodyController>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockBlackBody.Setup(b => b.SetTemperatureAsync(It.IsAny<int>(), It.IsAny<float>())).Returns(Task.CompletedTask);
            mockBlackBody.Setup(b => b.GetCurrentTemperatureAsync(It.IsAny<int>())).ReturnsAsync(47.0f);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new()
                    {
                        Kind = RecipeStepKind.BlackBodyControl,
                        TargetBlackBodyTemperature = 50.0f,
                        TargetBlackBodyTemperature1 = 50.0f,
                        StabilizationToleranceC = 5,
                        WaitForStabilization = true
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, blackBody: mockBlackBody.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockBlackBody.Verify(b => b.GetCurrentTemperatureAsync(0), Times.AtLeastOnce);
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

        [Theory]
        [InlineData(0, 1800, 1)]
        [InlineData(60, 1800, 30)]
        [InlineData(60, 600, 10)]
        // 전체 시간이 간격보다 짧아도 최소 1회는 찍는다.
        [InlineData(60, 0, 1)]
        [InlineData(60, 30, 1)]
        public void CaptureRepeatCount_DerivesRoundsFromDurationOverInterval(int interval, int duration, int expected)
        {
            var step = new RecipeStep
            {
                Kind = RecipeStepKind.CameraCommand,
                CaptureIntervalSeconds = interval,
                CaptureDurationSeconds = duration
            };

            Assert.Equal(expected, RecipeEngine.CaptureRepeatCount(step));
        }

        [Fact]
        public async Task ExecuteRecipeAsync_CameraCapture_WithInterval_RepeatsOnSchedule()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(mockNats);

            // 간격 1초 × 전체 3초 = 3회. 실제로 기다리는 테스트라 초 단위로 짧게 잡는다.
            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new()
                    {
                        Kind = RecipeStepKind.CameraCommand,
                        CameraOperation = CameraControlOps.Capture,
                        CameraIndex = 1,
                        ShotCount = 1,
                        CaptureIntervalSeconds = 1,
                        CaptureDurationSeconds = 3
                    }
                }
            };

            var clock = System.Diagnostics.Stopwatch.StartNew();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(15));
            clock.Stop();

            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Exactly(3));
            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Exactly(3));
            // 0초/1초/2초에 실행되므로 2초 이상 걸리고, 누적 지연이 없으니 넉넉잡아 5초를 넘지 않는다.
            Assert.InRange(clock.Elapsed.TotalSeconds, 1.8, 5.0);
        }

        [Theory]
        // 목표 20℃, 안전범위 10~60. 현재 80℃는 상한 초과이므로 이탈.
        [InlineData(80.0f, 50.0f, true)]
        // 승온·냉각 과도구간(목표와 60℃ 차이나도 범위 안이면 정상) — 상대 오차 방식이었다면 오탐하던 값.
        [InlineData(55.0f, 50.0f, false)]
        [InlineData(9.9f, 50.0f, true)]
        [InlineData(10.0f, 50.0f, false)]
        [InlineData(20.0f, 95.0f, true)]
        public void DescribeSafetyViolation_UsesAbsoluteLimitsNotTargetOffset(float temperature, float humidity, bool expectViolation)
        {
            var step = new RecipeStep
            {
                Kind = RecipeStepKind.ChamberControl,
                TargetChamberTemperature = 20,
                TargetChamberHumidity = 50,
                UseSafetyTemperature = true,
                SafetyTempMin = 10,
                SafetyTempMax = 60,
                UseSafetyHumidity = true,
                SafetyHumidityMin = 20,
                SafetyHumidityMax = 90
            };

            string? violation = RecipeEngine.DescribeSafetyViolation(step, temperature, humidity);

            Assert.Equal(expectViolation, violation != null);
        }

        [Fact]
        public void DescribeSafetyViolation_WhenUnchecked_IgnoresLimits()
        {
            var step = new RecipeStep
            {
                UseSafetyTemperature = false,
                SafetyTempMin = 10,
                SafetyTempMax = 60,
                UseSafetyHumidity = false,
                SafetyHumidityMin = 20,
                SafetyHumidityMax = 90
            };

            Assert.Null(RecipeEngine.DescribeSafetyViolation(step, 500f, 500f));
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenNoRecordingCondition_WritesNoMeasurement()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var mockMeasurements = new Mock<IRecipeMeasurementRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 25, TargetChamberHumidity = 50, WaitForChamberStabilization = false }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, measurementRepo: mockMeasurements.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockMeasurements.Verify(m => m.InsertAsync(It.IsAny<RecipeMeasurementRecord>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WithRecordingInterval_WritesMeasurementWithChamberAndCameraTemperature()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var mockMeasurements = new Mock<IRecipeMeasurementRepository>();
            var written = new List<RecipeMeasurementRecord>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(42.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(33.0f);
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockMeasurements.Setup(m => m.InsertAsync(It.IsAny<RecipeMeasurementRecord>()))
                            .Callback<RecipeMeasurementRecord>(r => written.Add(r))
                            .Returns(Task.CompletedTask);

            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { AgentId = "Agent_1", Alias = "cam1", CameraTemperature = 36.5 });

            var recipe = new Recipe
            {
                RecordIntervalSeconds = 1,
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 42, TargetChamberHumidity = 33, WaitForChamberStabilization = false }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object,
                                   agentDirectory: directory, measurementRepo: mockMeasurements.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            // 첫 샘플은 이후 변화량의 기준점이라 조건과 무관하게 항상 1건 남는다.
            Assert.NotEmpty(written);
            Assert.Equal(42.0f, written[0].ChamberTemperature);
            Assert.Equal(33.0f, written[0].ChamberHumidity);
            Assert.Equal(36.5, written[0].CameraTemperatures["Agent_1"]);
            Assert.All(written, r => Assert.Equal(written[0].RunId, r.RunId));
        }

        [Fact]
        public async Task ExecuteRecipeAsync_CameraCapture_WithShotCount_PublishesCountAndStoresEveryShot()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockPlc.Setup(p => p.GetCurrentTemperatureAsync()).ReturnsAsync(25.0f);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(50.0f);
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>())).Returns(Task.CompletedTask);
            WireCaptureRoundTrip(mockNats);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new()
                    {
                        Kind = RecipeStepKind.CameraCommand,
                        CameraOperation = CameraControlOps.Capture,
                        CameraIndex = 1,
                        ShotCount = 3
                    }
                }
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.Is<CaptureCommandMessage>(m => m.ShotCount == 3)), Times.Once);
            mockHistory.Verify(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Exactly(3));
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Warning);
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
                        UseSafetyTemperature = true,
                        SafetyTempMin = 23.0f,
                        SafetyTempMax = 27.0f,
                        WaitForChamberStabilization = false
                    }
                }
            };

            Func<CancellationToken, Task<bool>> resume = ct =>
            {
                resumeInvoked = true;
                return Task.FromResult(false);
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe, CancellationToken.None, null, resume)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(resumeInvoked);
            Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_ChamberControl_WritesTemperatureOnly()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

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

            mockPlc.Verify(p => p.SetTargetTemperatureAsync(30f), Times.AtLeastOnce);
            mockPlc.Verify(p => p.SetTargetHumidityAsync(It.IsAny<float>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_HumidityControl_WritesHumidityOnly()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.HumidityControl,
                        TargetChamberHumidity = 60,
                        WaitForChamberStabilization = false
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockPlc.Verify(p => p.SetTargetHumidityAsync(60f), Times.Once);
            mockPlc.Verify(p => p.SetHumidityControlAsync(true), Times.Once);
            mockPlc.Verify(p => p.SetTargetTemperatureAsync(It.IsAny<float>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_HumidityControl_WhenDisabled_TurnsControlOffWithoutWritingTarget()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.HumidityControl,
                        TargetChamberHumidity = 60,
                        DisableHumidityControl = true,

                        // 대기 옵션이 켜져 있어도 제어를 끄면 도달을 기다리지 않아야 한다.
                        WaitForChamberStabilization = true
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockPlc.Verify(p => p.SetHumidityControlAsync(false), Times.Once);
            mockPlc.Verify(p => p.SetTargetHumidityAsync(It.IsAny<float>()), Times.Never);
            mockPlc.Verify(p => p.GetCurrentHumidityAsync(), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_HumidityControl_WaitsUntilWithinStepTolerance()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.GetCurrentHumidityAsync()).ReturnsAsync(57f);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new RecipeStep
                    {
                        Kind = RecipeStepKind.HumidityControl,
                        TargetChamberHumidity = 60,
                        StabilizationToleranceRh = 5,
                        WaitForChamberStabilization = true
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            mockPlc.Verify(p => p.GetCurrentHumidityAsync(), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_BiasStep_PublishesStepTargetLevel()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var published = new List<CameraControlMessage>();
            var ackCallbacks = new Dictionary<string, Action<CameraControlAckMessage>>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockNats.Setup(n => n.SubscribeCameraControlAckAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlAckMessage>>()))
                .Callback<string, Action<CameraControlAckMessage>>((agentId, callback) => ackCallbacks[agentId] = callback)
                .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
                .Callback<CameraControlMessage>(message =>
                {
                    published.Add(message);
                    ackCallbacks[message.AgentId](new CameraControlAckMessage
                    {
                        AgentId = message.AgentId,
                        CameraIndex = message.CameraIndex,
                        Op = message.Op,
                        RequestId = message.RequestId,
                        IsSuccess = true
                    });
                })
                .Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new()
                    {
                        Kind = RecipeStepKind.CameraCommand,
                        CameraOperation = CameraControlOps.BiasMid,
                        BiasTargetLevel = 5200,
                        CameraTargets = new List<RecipeCameraTarget> { new() { AgentId = "Agent_1", CameraIndex = 1 } }
                    }
                }
            };

            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe)
                .WaitAsync(TimeSpan.FromSeconds(5));

            var message = Assert.Single(published);
            Assert.Equal(CameraControlOps.BiasMid, message.Op);
            Assert.Equal(5200, message.BiasTargetLevel);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenCameraOffline_RaisesPreflightAlarmAndWaits()
        {
            var (mockPlc, mockNats, mockHistory, published) = WireCameraControlRoundTrip();
            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { AgentId = "Agent_1" });

            bool resumeInvoked = false;
            Func<CancellationToken, Task<bool>> resume = _ =>
            {
                resumeInvoked = true;
                return Task.FromResult(false);
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, agentDirectory: directory)
                .ExecuteRecipeAsync(TwoCameraNucRecipe(), CancellationToken.None, null, resume)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(resumeInvoked);
            Assert.Contains(AlarmSink.Entries, e => e.Code == AlarmCodes.CameraOffline);

            // 스킵을 고르지 않았으므로 오프라인 카메라에도 명령은 나간다.
            Assert.Contains(published, m => m.AgentId == "Agent_2");
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenOperatorSkips_ExcludesOnlyTheOfflineCamera()
        {
            var (mockPlc, mockNats, mockHistory, published) = WireCameraControlRoundTrip();
            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { AgentId = "Agent_1" });

            Func<CancellationToken, Task<bool>> skip = _ => Task.FromResult(true);

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, agentDirectory: directory)
                .ExecuteRecipeAsync(TwoCameraNucRecipe(), CancellationToken.None, null, skip)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(published, m => m.AgentId == "Agent_1");
            Assert.DoesNotContain(published, m => m.AgentId == "Agent_2");
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WhenAllCamerasOnline_SkipsPreflightAlarm()
        {
            var (mockPlc, mockNats, mockHistory, published) = WireCameraControlRoundTrip();
            var directory = new AgentDirectory();
            directory.Note(new AgentStatusMessage { AgentId = "Agent_1" });
            directory.Note(new AgentStatusMessage { AgentId = "Agent_2" });

            bool resumeInvoked = false;
            Func<CancellationToken, Task<bool>> resume = _ =>
            {
                resumeInvoked = true;
                return Task.FromResult(false);
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object, agentDirectory: directory)
                .ExecuteRecipeAsync(TwoCameraNucRecipe(), CancellationToken.None, null, resume)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(resumeInvoked);
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Code == AlarmCodes.CameraOffline);
            Assert.Equal(2, published.Count);
        }

        private static Recipe TwoCameraNucRecipe() => new Recipe
        {
            Name = "Preflight",
            Steps = new List<RecipeStep>
            {
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

        private static (Mock<IPlcController>, Mock<INatsCommunicationService>, Mock<ICaptureHistoryRepository>, List<CameraControlMessage>)
            WireCameraControlRoundTrip()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            var published = new List<CameraControlMessage>();
            var ackCallbacks = new Dictionary<string, Action<CameraControlAckMessage>>();

            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);
            mockNats.Setup(n => n.SubscribeCameraControlAckAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlAckMessage>>()))
                .Callback<string, Action<CameraControlAckMessage>>((agentId, callback) => ackCallbacks[agentId] = callback)
                .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.PublishCameraControlAsync(It.IsAny<CameraControlMessage>()))
                .Callback<CameraControlMessage>(message =>
                {
                    published.Add(message);
                    ackCallbacks[message.AgentId](new CameraControlAckMessage
                    {
                        AgentId = message.AgentId,
                        CameraIndex = message.CameraIndex,
                        Op = message.Op,
                        RequestId = message.RequestId,
                        IsSuccess = true
                    });
                })
                .Returns(Task.CompletedTask);

            return (mockPlc, mockNats, mockHistory, published);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 5)]
        [InlineData(-1, 0)]
        public void SoakDuration_ConvertsMinutesAndTreatsNonPositiveAsNoWait(int soakMinutes, int expectedMinutes)
        {
            var step = new RecipeStep { Kind = RecipeStepKind.ChamberControl, SoakMinutes = soakMinutes };

            Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), RecipeEngine.SoakDuration(step));
        }

        [Fact]
        public async Task ExecuteRecipeAsync_AutomaticMotorMove_WaitsUntilCurrentPointReachesTarget()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.MoveServoToPositionAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            mockPlc.SetupSequence(p => p.ReadStatusAsync())
                .ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false, CurrentPoint = 0 })
                .ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false, CurrentPoint = 0 })
                .ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false, CurrentPoint = 5 });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.MotorMove, MotorMoveType = MotorMoveType.Automatic, TargetPositionIndex = 5 }
                }
            };

            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe).WaitAsync(TimeSpan.FromSeconds(10));

            mockPlc.Verify(p => p.MoveServoToPositionAsync(5), Times.Once);
            mockPlc.Verify(p => p.ReadStatusAsync(), Times.AtLeast(3));
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_AutomaticMotorMove_TimesOutRaisesAlarmAndAborts()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();

            mockPlc.Setup(p => p.MoveServoToPositionAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
            mockPlc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXBusy = false, ServoYBusy = false, CurrentPoint = 0 });
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.MotorMove, MotorMoveType = MotorMoveType.Automatic, TargetPositionIndex = 9 },
                    new() { Kind = RecipeStepKind.CameraCommand, CameraOperation = CameraControlOps.Capture, CameraIndex = 1 }
                }
            };

            var engine = new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object,
                new RecipeEngineSettings { MotorMoveTimeoutSeconds = 0 });

            AlarmSink.Entries.Clear();
            await Assert.ThrowsAsync<TimeoutException>(
                () => engine.ExecuteRecipeAsync(recipe).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Contains(AlarmSink.Entries, e => e.Code == AlarmCodes.MotorMoveTimeout && e.Severity == AlarmSeverity.Error);
            mockNats.Verify(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteRecipeAsync_WaitStep_BlocksForDurationWithoutTouchingPlc()
        {
            var mockPlc = new Mock<IPlcController>();
            var mockNats = new Mock<INatsCommunicationService>();
            var mockHistory = new Mock<ICaptureHistoryRepository>();
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>())).Returns(Task.CompletedTask);

            var recipe = new Recipe
            {
                Steps = new List<RecipeStep>
                {
                    new() { Kind = RecipeStepKind.Wait, WaitDurationSeconds = 1 }
                }
            };

            var clock = System.Diagnostics.Stopwatch.StartNew();
            AlarmSink.Entries.Clear();
            await new RecipeEngine(mockPlc.Object, mockNats.Object, mockHistory.Object)
                .ExecuteRecipeAsync(recipe).WaitAsync(TimeSpan.FromSeconds(10));
            clock.Stop();

            Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1), $"wait step should block for at least 1s but took {clock.Elapsed}");
            mockPlc.Verify(p => p.ReadStatusAsync(), Times.Never);
            mockPlc.Verify(p => p.StartChamberAsync(), Times.Never);
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error);
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
                    TargetChamberTemperature = 25.0
                },
                // 습도 한계는 습도 스텝이 정한다. 온도 스텝에 넣으면 감시되지 않는다.
                new RecipeStep
                {
                    Kind = RecipeStepKind.HumidityControl,
                    TargetChamberHumidity = 50.0,
                    WaitForChamberStabilization = false,
                    UseSafetyHumidity = true,
                    SafetyHumidityMin = 45.0f,
                    SafetyHumidityMax = 55.0f
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
                .Callback<CaptureCommandMessage>(cmd =>
                {
                    // 실 Agent와 동일하게 요청된 장수만큼 결과를 되돌려 준다.
                    int shots = cmd.ShotCount > 0 ? cmd.ShotCount : 1;
                    for (int i = 0; i < shots; i++)
                    {
                        resultCb?.Invoke(new CaptureResultMessage
                        {
                            AgentId      = "Agent_1",
                            RecipeStepId = cmd.RecipeStepId,
                            IsSuccess    = true,
                            ImagePath    = $"/test/image_{i}.jpg",
                            Timestamp    = DateTime.UtcNow
                        });
                    }
                })
                .Returns(Task.CompletedTask);
        }
    }
}
