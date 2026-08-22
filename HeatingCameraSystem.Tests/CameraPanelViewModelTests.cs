using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraPanelViewModelTests
    {
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
