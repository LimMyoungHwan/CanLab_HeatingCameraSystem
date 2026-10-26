using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraPanelDeepAuditTests
    {
        [Theory]
        [InlineData(CameraControlOps.Run)]
        [InlineData(CameraControlOps.ShutterClose)]
        [InlineData(CameraControlOps.SaveConfig)]
        [InlineData(CameraControlOps.RefreshInfo)]
        public async Task RemoteControl_NoSerial_PropagatesFailure(string operation)
        {
            using var fixture = new Fixture(withSerial: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(operation));
            Assert.Contains("오류", fixture.Panel.SerialStatus);
            Assert.False(fixture.Panel.IsBiasBusy);
        }

        [Theory]
        [InlineData(CameraControlOps.Run)]
        [InlineData(CameraControlOps.ShutterClose)]
        [InlineData(CameraControlOps.SaveConfig)]
        public async Task RemoteControl_SerialFailureIsNotConvertedToSuccess(string operation)
        {
            using var fixture = new Fixture();
            fixture.Serial.Setup(serial => serial.SetCameraRunningAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("missing hardware ACK"));
            fixture.Serial.Setup(serial => serial.SetShutterAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("missing hardware ACK"));
            fixture.Serial.Setup(serial => serial.SaveConfigAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("missing hardware ACK"));

            IOException error = await Assert.ThrowsAsync<IOException>(() => fixture.Panel.RunCameraControlAsync(operation));
            Assert.Equal("missing hardware ACK", error.Message);
            Assert.Contains("missing hardware ACK", fixture.Panel.SerialStatus);
            Assert.False(fixture.Panel.IsBiasBusy);
        }

        [Fact]
        public async Task BiasBlocksAllMutatingCommands_ThenReleasesGateOnCancellation()
        {
            using var fixture = new Fixture();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns((CameraBiasRegister _, byte _, CancellationToken ct) =>
                {
                    entered.TrySetResult();
                    return Task.Delay(Timeout.Infinite, ct);
                });
            using var cancellation = new CancellationTokenSource();
            Task bias = fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, cancellation.Token);
            await entered.Task;
            try
            {
                Assert.False(fixture.Panel.SaveConfigCommand.CanExecute(null));
                Assert.False(fixture.Panel.CloseShutterCommand.CanExecute(null));
                Assert.False(fixture.Panel.ApplyOutputFormatCommand.CanExecute(null));
                Assert.False(fixture.Panel.CaptureSaveCommand.CanExecute(null));
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.SaveConfig));
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.ShutterClose));
                fixture.Serial.Verify(serial => serial.SaveConfigAsync(It.IsAny<CancellationToken>()), Times.Never);
                fixture.Serial.Verify(serial => serial.SetShutterAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { cancellation.Cancel(); }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bias);
            await fixture.Panel.RunCameraControlAsync(CameraControlOps.SaveConfig);
            fixture.Serial.Verify(serial => serial.SaveConfigAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.Null(fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task UnloadClearsGenerationAndRejectsStaleCaptureAndNuc()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            Assert.NotNull(fixture.Panel.LastAppliedBias);
            fixture.Runtime.SetupGet(runtime => runtime.LatestFrame)
                .Returns(new ThermalFrame(new ushort[] { 8500 }, 1, 1, DateTimeOffset.Now));

            fixture.Panel.RebindRuntime(null);

            Assert.False(fixture.Panel.HasRuntime);
            Assert.Equal(-1, fixture.Panel.CameraIndex);
            Assert.Null(fixture.Panel.LastBiasJson);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.Capture));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.Nuc));
            fixture.Runtime.Verify(runtime => runtime.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.Serial.Verify(serial => serial.SetShutterAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.y16", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task RuntimeReplacementCancelsInFlightBias_BeforeReplacingAndNeverKeepsItsGeneration()
        {
            using var fixture = new Fixture();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns((CameraBiasRegister _, byte _, CancellationToken ct) =>
                {
                    entered.TrySetResult();
                    return Task.Delay(Timeout.Infinite, ct);
                });
            Task bias = fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            await entered.Task;
            var replacement = new Mock<ICameraRuntime>();
            replacement.SetupGet(runtime => runtime.CameraIndex).Returns(7);
            await fixture.Panel.ChangeRuntimeAsync(() => Task.FromResult<ICameraRuntime?>(replacement.Object));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bias);
            Assert.True(fixture.Panel.HasRuntime);
            Assert.Equal(7, fixture.Panel.CameraIndex);
            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.False(fixture.Panel.IsBiasBusy);
        }

        [Fact]
        public async Task RebindingSameRuntimePreservesAppliedGeneration()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);
            fixture.Panel.RebindRuntime(fixture.Runtime.Object);
            Assert.Same(applied, fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task CaptureLeaseBlocksLocalChanges_AndBindingLossCancelsRemoteCaptureToken()
        {
            using var fixture = new Fixture();
            ICameraOperationLease lease = await fixture.Panel.AcquireCaptureLeaseAsync(CancellationToken.None);
            try
            {
                Assert.True(fixture.Panel.IsBiasBusy);
                Assert.False(fixture.Panel.CloseShutterCommand.CanExecute(null));
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.ShutterClose));
                await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None));
                Assert.False(lease.CancellationToken.IsCancellationRequested);
                fixture.Panel.RebindRuntime(null);
                Assert.True(lease.CancellationToken.IsCancellationRequested);
            }
            finally { await lease.DisposeAsync(); }
            await lease.DisposeAsync(); // A duplicate pipeline finally cannot release the semaphore twice.
            fixture.Panel.RebindRuntime(fixture.Runtime.Object);
            await fixture.Panel.RunCameraControlAsync(CameraControlOps.SaveConfig);
            Assert.False(fixture.Panel.IsBiasBusy);
            fixture.Serial.Verify(serial => serial.SetShutterAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task NucWithoutNewFrameFailsAndReopensShutter()
        {
            using var fixture = new Fixture();
            fixture.Runtime.Setup(runtime => runtime.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ThermalFrame?)null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Panel.RunCameraControlAsync(CameraControlOps.Nuc));
            fixture.Serial.Verify(serial => serial.SetShutterAsync(false, It.IsAny<CancellationToken>()), Times.Once);
            fixture.Serial.Verify(serial => serial.SetShutterAsync(true, It.IsAny<CancellationToken>()), Times.Once);
            Assert.False(fixture.Panel.IsBiasBusy);
        }

        private sealed class Fixture : IDisposable
        {
            public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "hcs_deep_panel_" + Guid.NewGuid().ToString("N"));
            private readonly CaptureStore _store;
            public Mock<ICameraSerialClient> Serial { get; } = new();
            public Mock<ICameraRuntime> Runtime { get; } = new();
            public CameraPanelViewModel Panel { get; }
            private byte _bias;

            public Fixture(bool withSerial = true)
            {
                Directory.CreateDirectory(DirectoryPath);
                _store = new CaptureStore(DirectoryPath, new LiteDbCaptureIndex(Path.Combine(DirectoryPath, "index.db")));
                Runtime.SetupGet(runtime => runtime.CameraIndex).Returns(3);
                Runtime.SetupGet(runtime => runtime.Status).Returns(CameraRuntimeStatus.Running);
                Serial.Setup(serial => serial.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Serial.Setup(serial => serial.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Callback<byte, CancellationToken>((value, _) => _bias = value).Returns(Task.CompletedTask);
                Serial.Setup(serial => serial.ReadBiasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _bias);
                Serial.Setup(serial => serial.SetShutterAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Serial.Setup(serial => serial.SetCameraRunningAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Serial.Setup(serial => serial.SaveConfigAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
                Panel = new CameraPanelViewModel("camera", "Agent_3", Runtime.Object, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), _store, serial: withSerial ? Serial.Object : null);
            }

            public void Dispose()
            {
                Panel.Dispose();
                _store.Dispose();
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
