using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
    public class ManualBiasRestoreTests
    {
        [Theory]
        [InlineData("00", 0)]
        [InlineData("42", 66)]
        [InlineData("0x42", 66)]
        [InlineData(" 0Xff ", 255)]
        [InlineData("A", 10)]
        public void HexInput_ParsesRegisterByte(string text, int expected)
        {
            Assert.True(CameraPanelViewModel.TryParseManualBiasValue(text, out byte value));
            Assert.Equal((byte)expected, value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("8500")]
        [InlineData("256")]
        [InlineData("0x100")]
        [InlineData("-1")]
        [InlineData("+1")]
        [InlineData("0x")]
        [InlineData("4.2")]
        [InlineData("GG")]
        public void HexInput_RejectsValuesOutsideOneHexByte(string? text)
        {
            Assert.False(CameraPanelViewModel.TryParseManualBiasValue(text, out _));
        }

        [Theory]
        [InlineData(CameraControlOps.BiasLow, 0x93)]
        [InlineData(CameraControlOps.BiasMid, 0xA3)]
        [InlineData(CameraControlOps.BiasHigh, 0xD3)]
        public async Task Restore_WritesOriginalBandAndFixedRegisters_ThenConfirmsLsb(string operation, int cint)
        {
            using var fixture = new Fixture();

            await fixture.Panel.RestoreBiasAsync(operation, 0x42, CancellationToken.None);

            Assert.Equal(new[]
            {
                (CameraBiasRegister.TintMsb, (byte)0x02),
                (CameraBiasRegister.TintLsb, (byte)0x73),
                (CameraBiasRegister.Cint, (byte)cint),
                (CameraBiasRegister.GskMsb, (byte)0x01),
                (CameraBiasRegister.Gfid, (byte)0xAE)
            }, fixture.FixedWrites);
            Assert.Equal((byte)0x42, fixture.HardwareBias);
            Assert.Equal(1, fixture.BiasWrites);
            AppliedBiasResult result = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);
            Assert.Equal(operation, result.Operation);
            Assert.Equal(BiasApplicationSource.Manual, result.Source);
            Assert.Equal((byte)0x42, result.GskLsb);
            Assert.Equal(result.Json, fixture.Panel.LastBiasJson);
            using JsonDocument json = JsonDocument.Parse(result.Json);
            Assert.Equal(cint.ToString("X2"), json.RootElement.GetProperty("CINT").GetString());
            Assert.Equal("42", json.RootElement.GetProperty("GSK_LSB").GetString());
            AssertNoPixelSearchOrPersistentSave(fixture);
        }

        [Fact]
        public async Task Restore_ReadbackMismatch_InvalidatesPreviousSuccess()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            fixture.ReadOverride = 0x43;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None));

            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.Null(fixture.Panel.LastBiasJson);
            Assert.False(fixture.Panel.IsBiasBusy);
        }

        [Fact]
        public async Task Restore_FixedRegisterWriteFailure_DoesNotPublishResult()
        {
            using var fixture = new Fixture();
            fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.Cint, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("CINT write not acknowledged"));

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None));

            Assert.Equal(0, fixture.BiasWrites);
            Assert.Null(fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task Restore_NoSerial_FailsWithoutChangingRegisters()
        {
            using var fixture = new Fixture(withSerial: false);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None));

            Assert.Empty(fixture.FixedWrites);
            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.False(fixture.Panel.RestoreManualBiasCommand.CanExecute(null));
        }

        [Fact]
        public async Task Restore_UnsupportedBand_DoesNotWriteRegisters()
        {
            using var fixture = new Fixture();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                fixture.Panel.RestoreBiasAsync("unknown", 0x42, CancellationToken.None));

            Assert.Empty(fixture.FixedWrites);
            Assert.Equal(0, fixture.BiasWrites);
            Assert.Null(fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task Restore_SameJsonGetsNewGeneration_WhileVerificationPreservesIt()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            AppliedBiasResult first = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);

            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            AppliedBiasResult second = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);
            await fixture.Panel.VerifyAppliedBiasAsync(CameraControlOps.BiasLow, CancellationToken.None);

            Assert.Equal(first.Json, second.Json);
            Assert.NotEqual(first.ApplicationId, second.ApplicationId);
            Assert.Same(second, fixture.Panel.LastAppliedBias);
            AssertNoPixelSearchOrPersistentSave(fixture);
        }

        [Fact]
        public async Task Restore_AfterAutomaticSuccess_UsesNewManualGeneration()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None);
            AppliedBiasResult automatic = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);

            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, automatic.GskLsb, CancellationToken.None);
            AppliedBiasResult manual = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);

            Assert.Equal(BiasApplicationSource.Automatic, automatic.Source);
            Assert.Equal(BiasApplicationSource.Manual, manual.Source);
            Assert.Equal(automatic.Json, manual.Json);
            Assert.NotEqual(automatic.ApplicationId, manual.ApplicationId);
        }

        [Fact]
        public async Task InputEdits_DoNotChangeAppliedState_AndPixelTargetCannotBeSubmitted()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);

            fixture.Panel.ManualBiasOperation = CameraControlOps.BiasHigh;
            fixture.Panel.ManualBiasValueText = "8500";
            Assert.False(fixture.Panel.RestoreManualBiasCommand.CanExecute(null));
            await fixture.Panel.RestoreManualBiasCommand.ExecuteAsync(null);

            Assert.Same(applied, fixture.Panel.LastAppliedBias);
            Assert.Equal((byte)0x42, fixture.HardwareBias);
            Assert.Equal(1, fixture.BiasWrites);
            fixture.Panel.ManualBiasValueText = "0xFF";
            Assert.True(fixture.Panel.RestoreManualBiasCommand.CanExecute(null));
            Assert.Equal("0xFF (255)", fixture.Panel.ManualBiasValuePreview);
        }

        [Fact]
        public async Task Verify_WrongBand_InvalidatesRecordWithoutWriting()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.VerifyAppliedBiasAsync(CameraControlOps.BiasMid, CancellationToken.None));

            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.Equal(1, fixture.BiasWrites);
            AssertNoPixelSearchOrPersistentSave(fixture);
        }

        [Fact]
        public async Task Verify_ChangedHardware_InvalidatesRecord()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            fixture.ReadOverride = 0x43;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.VerifyAppliedBiasAsync(CameraControlOps.BiasLow, CancellationToken.None));

            Assert.Null(fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task Verify_TransientReadFailure_PreservesGenerationForRetry()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.Panel.LastAppliedBias);
            fixture.FailRead = true;

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.Panel.VerifyAppliedBiasAsync(CameraControlOps.BiasLow, CancellationToken.None));

            Assert.Same(applied, fixture.Panel.LastAppliedBias);
            fixture.FailRead = false;
            await fixture.Panel.VerifyAppliedBiasAsync(CameraControlOps.BiasLow, CancellationToken.None);
            Assert.Same(applied, fixture.Panel.LastAppliedBias);
        }

        [Fact]
        public async Task Restore_CancelledBeforeStart_InvalidatesPreviousResult()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x43, cancellation.Token));

            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.Equal(1, fixture.BiasWrites);
        }

        [Fact]
        public async Task Restore_InProgress_BlocksAutomaticBias_AndCancellationReleasesGate()
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(async (CameraBiasRegister register, byte value, CancellationToken ct) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                });
            Task running = fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(fixture.Panel.IsBiasBusy);
            Assert.False(fixture.Panel.RunBiasLowCommand.CanExecute(null));
            Assert.False(fixture.Panel.CanEditBias);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.False(fixture.Panel.IsBiasBusy);
            fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);
            Assert.NotNull(fixture.Panel.LastAppliedBias);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task ManualCommand_UsesFourAttemptsAndKeepsControlsBusyBetweenRetries(bool recoverOnFourth)
            => RunOnDispatcherAsync(async () =>
            {
                using var fixture = new Fixture();
                int attempts = 0;
                fixture.Panel.ManualBiasValueText = "42";
                fixture.Serial.Setup(serial => serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns((CameraBiasRegister register, byte value, CancellationToken ct) =>
                    {
                        Assert.True(fixture.Panel.IsBiasBusy);
                        Assert.False(fixture.Panel.RunBiasLowCommand.CanExecute(null));
                        attempts++;
                        return recoverOnFourth && attempts == 4
                            ? Task.CompletedTask
                            : Task.FromException(new IOException("serial timeout"));
                    });

                await fixture.Panel.RestoreManualBiasCommand.ExecuteAsync(null);

                Assert.Equal(4, attempts);
                Assert.Equal(recoverOnFourth, fixture.Panel.LastAppliedBias is not null);
                Assert.False(fixture.Panel.IsBiasBusy);
                Assert.True(fixture.Panel.CanEditBias);
                AssertNoPixelSearchOrPersistentSave(fixture);
            });

        [Fact]
        public async Task Dispose_InvalidatesAppliedStateAndDisablesBiasCommands()
        {
            using var fixture = new Fixture();
            await fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None);

            fixture.Panel.Dispose();

            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.Null(fixture.Panel.LastBiasJson);
            Assert.False(fixture.Panel.CanEditBias);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                fixture.Panel.RestoreBiasAsync(CameraControlOps.BiasLow, 0x42, CancellationToken.None));
        }

        private static void AssertNoPixelSearchOrPersistentSave(Fixture fixture)
        {
            fixture.Runtime.Verify(runtime => runtime.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.Serial.Verify(serial => serial.SaveConfigAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        private static Task RunOnDispatcherAsync(Func<Task> test)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _ = dispatcher.InvokeAsync(async () =>
                {
                    try { await test(); completed.TrySetResult(); }
                    catch (Exception ex) { completed.TrySetException(ex); }
                    finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                });
                Dispatcher.Run();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "hcs_manual_bias_" + Guid.NewGuid().ToString("N"));
            private readonly LiteDbCaptureIndex _index;
            private readonly CaptureStore _store;
            public Mock<ICameraSerialClient> Serial { get; } = new();
            public Mock<ICameraRuntime> Runtime { get; } = new();
            public List<(CameraBiasRegister Register, byte Value)> FixedWrites { get; } = new();
            public CameraPanelViewModel Panel { get; }
            public byte HardwareBias { get; private set; }
            public byte? ReadOverride { get; set; }
            public bool FailRead { get; set; }
            public int BiasWrites { get; private set; }

            public Fixture(bool withSerial = true)
            {
                Directory.CreateDirectory(_directory);
                _index = new LiteDbCaptureIndex(Path.Combine(_directory, "index.db"));
                _store = new CaptureStore(_directory, _index);
                Runtime.SetupGet(runtime => runtime.Status).Returns(CameraRuntimeStatus.Running);
                Runtime.Setup(runtime => runtime.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .Returns((TimeSpan? age, TimeSpan? timeout, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return Task.FromResult<ThermalFrame?>(new ThermalFrame(new ushort[] { 8500 }, 1, 1, DateTimeOffset.Now.AddTicks(1)));
                    });
                Serial.Setup(serial => serial.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns((CameraBiasRegister register, byte value, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        FixedWrites.Add((register, value));
                        return Task.CompletedTask;
                    });
                Serial.Setup(serial => serial.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns((byte value, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        HardwareBias = value;
                        BiasWrites++;
                        return Task.CompletedTask;
                    });
                Serial.Setup(serial => serial.ReadBiasAsync(It.IsAny<CancellationToken>()))
                    .Returns((CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (FailRead) throw new IOException("temporary read timeout");
                        return Task.FromResult(ReadOverride ?? HardwareBias);
                    });
                Serial.Setup(serial => serial.ReadSerialNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("544112136");
                Serial.Setup(serial => serial.ReadFpaTemperatureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(25.0);
                Panel = new CameraPanelViewModel("camera", "Agent_1", Runtime.Object, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), _store, serial: withSerial ? Serial.Object : null);
            }

            public void Dispose()
            {
                Panel.Dispose();
                _store.Dispose();
                _index.Dispose();
                try { Directory.Delete(_directory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
