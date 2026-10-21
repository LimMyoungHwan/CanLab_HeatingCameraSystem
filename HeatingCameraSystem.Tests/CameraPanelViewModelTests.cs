using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraPanelViewModelTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FindBiasInRangeAsync_MonotonicResponse_SelectsTargetRange(bool increasing)
        {
            Task<double> Measure(byte value) => Task.FromResult(
                increasing ? 3000 + value * 8.0 : 5000 - value * 8.0);

            var result = await CameraPanelViewModel.FindBiasInRangeAsync(3900, 4100, Measure);

            Assert.True(result.Success);
            Assert.Equal(0, result.Error);
            Assert.InRange(result.Level, 3900, 4100);
            Assert.InRange(result.Value, (byte)110, (byte)140);
        }

        [Fact]
        public async Task FindBiasInRangeAsync_UnreachableTarget_ReportsBoundaryErrorAndMeasuredLevel()
        {
            var result = await CameraPanelViewModel.FindBiasInRangeAsync(
                4900, 5100, _ => Task.FromResult(7000.0));

            Assert.False(result.Success);
            Assert.Equal(7000, result.Level);
            Assert.Equal(1900, result.Error);
        }

        [Fact]
        public async Task BiasCommand_DiscardsOldFrames_ThenFailureClearsPreviousJsonAndAppliesBest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_bias_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte written = 0;
                int pendingOldFrames = 0, discarded = 0;
                bool reachable = true;
                DateTimeOffset writtenAt = DateTimeOffset.MinValue;
                var serial = new Mock<ICameraSerialClient>();
                serial.Setup(s => s.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Callback<byte, CancellationToken>((value, _) =>
                    {
                        written = value;
                        writtenAt = DateTimeOffset.Now;
                        pendingOldFrames = 8;
                    })
                    .Returns(Task.CompletedTask);
                var runtime = new Mock<ICameraRuntime>();
                runtime.Setup(r => r.CaptureSnapshotAsync(
                    It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Assert.True(DateTimeOffset.Now - writtenAt >= TimeSpan.FromMilliseconds(90));
                        // Buffered frames keep their pre-write timestamp and incorrect old level.
                        bool old = pendingOldFrames-- > 0;
                        if (old) discarded++;
                        ushort level = old ? (ushort)16000
                            : reachable ? (ushort)(3000 + written * 20) : (ushort)7000;
                        ThermalFrame? frame = new ThermalFrame(new[] { level }, 1, 1,
                            old ? writtenAt.AddSeconds(-1) : DateTimeOffset.Now);
                        return Task.FromResult<ThermalFrame?>(frame);
                    });
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                using var panel = new CameraPanelViewModel("cam", "Agent_1", runtime.Object,
                    Dispatcher.CurrentDispatcher, new ThermalNucCorrector(), store, serial: serial.Object);

                await panel.RunBiasMidCommand.ExecuteAsync(null);

                Assert.True(panel.LastBiasSucceeded);
                Assert.NotNull(panel.LastBiasJson);
                Assert.Contains("BIAS 완료:", panel.SerialStatus);
                Assert.InRange(written, (byte)95, (byte)105);
                Assert.True(discarded >= 8);

                reachable = false;
                await panel.RunBiasMidCommand.ExecuteAsync(null);

                Assert.False(panel.LastBiasSucceeded);
                Assert.Null(panel.LastBiasJson);
                Assert.Equal("BIAS 실패: 0x00 (측정 7000, 목표 5000±100)", panel.SerialStatus);
                Assert.Equal((byte)0, written); // Best applied, not the final probed LSB.
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BiasCommand_OnlyStaleFallbackFrames_ReportsFailureWithoutJson()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_bias_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var runtime = new Mock<ICameraRuntime>();
                runtime.Setup(r => r.CaptureSnapshotAsync(
                    It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ThermalFrame(new ushort[] { 5000 }, 1, 1, DateTimeOffset.MinValue));
                var serial = new Mock<ICameraSerialClient>();
                serial.Setup(s => s.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                using var panel = new CameraPanelViewModel("cam", "Agent_1", runtime.Object,
                    Dispatcher.CurrentDispatcher, new ThermalNucCorrector(), store, serial: serial.Object);

                await panel.RunBiasMidCommand.ExecuteAsync(null);

                Assert.False(panel.LastBiasSucceeded);
                Assert.Null(panel.LastBiasJson);
                Assert.Contains("BIAS 실패:", panel.SerialStatus);
                Assert.Contains("최신 프레임", panel.SerialStatus);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task StartLiveAsync_WithNullSerial_CompletesAndControlDisabled()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_panel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                using var panel = new CameraPanelViewModel("cam", "Agent_1", runtime, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), store, captureBurstCount: 1, serial: null);

                await panel.StartLiveAsync();

                Assert.False(panel.HasSerialControl);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void ApplyInfo_FailedReadBlanksAllLiveValues()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_panel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                using var panel = new CameraPanelViewModel("cam", "Agent_1", runtime, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), store, captureBurstCount: 1, serial: null);

                panel.ApplyInfo(("545308020", 31.47, 0xA3));

                Assert.Equal("545308020", panel.SerialNumber);
                Assert.Equal("31.5 ℃", panel.FpaTemperature);
                Assert.Equal("0xA3", panel.BiasLsb);

                panel.ApplyInfo(null);

                Assert.Equal("—", panel.SerialNumber);
                Assert.Equal("—", panel.FpaTemperature);
                Assert.Equal("—", panel.BiasLsb);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task InfoPoll_TicksEverySecondAndStopsOnDispose()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_panel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);

                var serial = new Mock<ICameraSerialClient>();
                serial.Setup(s => s.ReadSerialNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("545308020");
                serial.Setup(s => s.ReadFpaTemperatureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(31.4);
                serial.Setup(s => s.ReadBiasAsync(It.IsAny<CancellationToken>())).ReturnsAsync((byte)0xA3);

                int BiasReads() => serial.Invocations.Count(i => i.Method.Name == nameof(ICameraSerialClient.ReadBiasAsync));
                int SerialNumberReads() => serial.Invocations.Count(i => i.Method.Name == nameof(ICameraSerialClient.ReadSerialNumberAsync));
                using (new CameraPanelViewModel("cam", "Agent_1", runtime, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), store, captureBurstCount: 1, serial: serial.Object))
                {
                    DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
                    while (BiasReads() < 2 && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(100);
                    }
                }

                Assert.True(BiasReads() >= 2, $"1초 주기 폴링이 아니다: 6초 안에 {BiasReads()}회만 읽었다.");
                Assert.True(SerialNumberReads() >= 2, $"S/N을 매 틱 읽지 않는다: {SerialNumberReads()}회.");

                await Task.Delay(TimeSpan.FromMilliseconds(200));
                int atDispose = BiasReads();
                await Task.Delay(TimeSpan.FromMilliseconds(1500));

                Assert.Equal(atDispose, BiasReads());
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task StartLiveAsync_SerialCommandFailure_SetsStatusAndKeepsControl()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_panel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);

                var serial = new Mock<ICameraSerialClient>();
                serial.SetupGet(s => s.PortName).Returns("COM9");
                serial.Setup(s => s.SetCameraRunningAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new IOException("port broke"));

                using var panel = new CameraPanelViewModel("cam", "Agent_1", runtime, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), store, captureBurstCount: 1, serial: serial.Object);

                await panel.StartLiveAsync();

                Assert.True(panel.HasSerialControl);
                Assert.Contains("영상 시작", panel.SerialStatus);
                Assert.Contains("오류", panel.SerialStatus);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
