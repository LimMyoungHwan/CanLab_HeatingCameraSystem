using System;
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
    public class CameraBiasValidationTests
    {
        [Theory]
        [InlineData(8400)]
        [InlineData(8500)]
        [InlineData(8600)]
        public async Task RunBiasOperation_ValidatedRange_CreatesCurrentResult(int level)
        {
            using var fixture = new BiasFixture { Level = (ushort)level };

            await fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None);

            Assert.NotNull(fixture.Panel.LastBiasJson);
            using JsonDocument result = JsonDocument.Parse(fixture.Panel.LastBiasJson ?? throw new InvalidOperationException("보정 결과가 없습니다."));
            Assert.Equal("93", result.RootElement.GetProperty("CINT").GetString());
            Assert.Equal("00", result.RootElement.GetProperty("GSK_LSB").GetString());
            Assert.Contains("BIAS 완료", fixture.Panel.SerialStatus);
        }

        [Fact]
        public async Task RunBiasOperation_NoSerial_FailsWithoutResult()
        {
            using var fixture = new BiasFixture(withSerial: false);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Contains("시리얼", error.Message);
            Assert.Null(fixture.Panel.LastBiasJson);
            Assert.Equal(0, fixture.BiasWrites);
        }

        [Fact]
        public async Task RunBiasOperation_UnreachableTarget_DoesNotReportSuccess()
        {
            using var fixture = new BiasFixture { Level = 1000 };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Contains("최종 검증 실패", error.Message);
            Assert.Contains("목표 8500±100", error.Message);
            Assert.Contains("측정 1000", error.Message);
            Assert.Contains("NG", fixture.Panel.SerialStatus);
            Assert.Null(fixture.Panel.LastAppliedBias);
            Assert.False(fixture.Panel.LastBiasSucceeded);
            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_FinalLevelDrift_RejectsPreviouslyValidSearch()
        {
            using var fixture = new BiasFixture();
            fixture.FrameFactory = () => fixture.Frame(fixture.BiasWrites < 2 ? (ushort)8500 : (ushort)9000);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Contains("최종 검증 실패", error.Message);
            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_FinalWriteFails_DoesNotCreateResult()
        {
            using var fixture = new BiasFixture();
            int writes = 0;
            fixture.Serial.Setup(s => s.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns((byte value, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (++writes == 2) throw new IOException("최종 쓰기 응답 시간 초과");
                    return Task.CompletedTask;
                });

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Null(fixture.Panel.LastBiasJson);
            Assert.Contains("NG", fixture.Panel.SerialStatus);
        }

        [Fact]
        public async Task RunBiasOperation_FailureAfterSuccess_InvalidatesPreviousResult()
        {
            using var fixture = new BiasFixture();
            await fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None);
            Assert.NotNull(fixture.Panel.LastBiasJson);
            fixture.Serial.Setup(s => s.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("포트 오류"));

            await Assert.ThrowsAsync<IOException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_StaleSnapshot_IsFailureEvenWhenLevelIsCorrect()
        {
            using var fixture = new BiasFixture();
            var oldFrame = new ThermalFrame(new ushort[] { 8500 }, 1, 1, DateTimeOffset.Now.AddMinutes(-1));
            fixture.FrameFactory = () => oldFrame;

            var error = await Assert.ThrowsAsync<TimeoutException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Contains("새 프레임", error.Message);
            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_NonRadiometricSnapshot_IsFailure()
        {
            using var fixture = new BiasFixture();
            fixture.FrameFactory = () => fixture.Frame(8500) with { Bgr24 = new byte[] { 0, 0, 0 } };

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));

            Assert.Contains("Y16", error.Message);
            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_CancelledBeforeStart_InvalidatesPreviousResult()
        {
            using var fixture = new BiasFixture();
            await fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None);
            Assert.NotNull(fixture.Panel.LastBiasJson);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, cancellation.Token));

            Assert.Null(fixture.Panel.LastBiasJson);
        }

        [Fact]
        public async Task RunBiasOperation_ConcurrentRequestIsRejected_AndCancellationReleasesCamera()
        {
            using var fixture = new BiasFixture();
            using var cancellation = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Serial.Setup(s => s.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(async (CameraBiasRegister register, byte value, CancellationToken ct) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                });
            Task running = fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var busy = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None));
            Assert.Contains("이미 BIAS", busy.Message);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.Null(fixture.Panel.LastBiasJson);

            fixture.Serial.Setup(s => s.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            await fixture.Panel.RunBiasOperationAsync(CameraControlOps.BiasLow, 8500, CancellationToken.None);
            Assert.NotNull(fixture.Panel.LastBiasJson);
        }

        private sealed class BiasFixture : IDisposable
        {
            private readonly string _directory = Path.Combine(Path.GetTempPath(), "hcs_bias_" + Guid.NewGuid().ToString("N"));
            private readonly LiteDbCaptureIndex _index;
            private readonly CaptureStore _store;

            public Mock<ICameraSerialClient> Serial { get; } = new();
            public Mock<ICameraRuntime> Runtime { get; } = new();
            public CameraPanelViewModel Panel { get; }
            public ushort Level { get; set; } = 8500;
            public int BiasWrites { get; private set; }
            public Func<ThermalFrame> FrameFactory { get; set; }

            public BiasFixture(bool withSerial = true)
            {
                Directory.CreateDirectory(_directory);
                _index = new LiteDbCaptureIndex(Path.Combine(_directory, "idx.db"));
                _store = new CaptureStore(_directory, _index);
                FrameFactory = () => Frame(Level);
                Runtime.SetupGet(r => r.Status).Returns(CameraRuntimeStatus.Running);
                Runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .Returns((TimeSpan? maxAge, TimeSpan? timeout, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return Task.FromResult<ThermalFrame?>(FrameFactory());
                    });
                Serial.Setup(s => s.SetBiasRegisterAsync(It.IsAny<CameraBiasRegister>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                Serial.Setup(s => s.SetBiasAsync(It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                    .Returns((byte value, CancellationToken ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        BiasWrites++;
                        return Task.CompletedTask;
                    });
                Serial.Setup(s => s.ReadSerialNumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("545308020");
                Serial.Setup(s => s.ReadFpaTemperatureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(25.0);
                Serial.Setup(s => s.ReadBiasAsync(It.IsAny<CancellationToken>())).ReturnsAsync((byte)0);
                Panel = new CameraPanelViewModel("cam", "Agent_1", Runtime.Object, Dispatcher.CurrentDispatcher,
                    new ThermalNucCorrector(), _store, serial: withSerial ? Serial.Object : null);
            }

            public ThermalFrame Frame(ushort level)
                => new(new[] { level }, 1, 1, DateTimeOffset.Now.AddTicks(1));

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
