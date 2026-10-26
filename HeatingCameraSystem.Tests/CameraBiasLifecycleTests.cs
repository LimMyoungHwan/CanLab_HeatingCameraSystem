using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
    public class CameraBiasLifecycleTests
    {
        private const string ValidBias = "{\"CINT\":\"93\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"10\",\"GFID\":\"AE\"}";

        [Fact]
        public async Task BiasAbortBeforeOriginal_PreventsDelayedCalibration()
        {
            using var fixture = new Fixture();
            int calibrationCalls = 0;
            await using var connector = fixture.Create((_, _, _) =>
            {
                calibrationCalls++;
                return Task.CompletedTask;
            });

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Abort(CameraControlOps.BiasAbort, "bias-request"));
            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Bias());

            Assert.Equal(0, calibrationCalls);
            CameraControlAckMessage result = Assert.Single(fixture.Acks.Where(ack => ack.Op == CameraControlOps.BiasLow));
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task CaptureAbortBeforeOriginal_PreventsDelayedCapture()
        {
            using var fixture = new Fixture();
            int temperatureReads = 0;
            await using var connector = fixture.Create((_, _, _) => Task.CompletedTask,
                _ => { temperatureReads++; return Task.FromResult<double?>(25); });

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Abort(CameraControlOps.CaptureAbort, "capture-request"));
            await connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("capture-request"));

            Assert.Equal(0, temperatureReads);
            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.False(result.IsSuccess);
            Assert.Contains("NG", result.Message);
            Assert.Empty(fixture.Store.Query());
        }

        [Fact]
        public async Task BiasWaitingForCaptureGate_CanBeCancelledBeforeCalibration()
        {
            using var fixture = new Fixture();
            var captureEntered = Signal();
            var releaseCapture = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calibrationCalls = 0;
            await using var connector = fixture.Create((_, _, _) =>
            {
                calibrationCalls++;
                return Task.CompletedTask;
            }, _ => { captureEntered.TrySetResult(); return releaseCapture.Task; });

            Task capture = connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("holding-capture"));
            await captureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task bias = connector.HandleCameraControlAsync(fixture.Camera, fixture.Bias());
            try
            {
                await connector.HandleCameraControlAsync(fixture.Camera,
                    fixture.Abort(CameraControlOps.BiasAbort, "bias-request")).WaitAsync(TimeSpan.FromSeconds(2));
                await bias.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(capture.IsCompleted);
                Assert.Equal(0, calibrationCalls);
                Assert.False(Assert.Single(fixture.Acks.Where(ack => ack.Op == CameraControlOps.BiasLow)).IsSuccess);
            }
            finally
            {
                releaseCapture.TrySetResult(25);
                await Task.WhenAll(capture, bias).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task CaptureWaitingForBiasGate_CanBeCancelledBeforeReadingCamera()
        {
            using var fixture = new Fixture();
            var biasEntered = Signal();
            var releaseBias = Signal();
            int temperatureReads = 0;
            await using var connector = fixture.Create(async (_, _, token) =>
            {
                biasEntered.TrySetResult();
                await releaseBias.Task.WaitAsync(token);
            }, _ => { temperatureReads++; return Task.FromResult<double?>(25); });

            Task bias = connector.HandleCameraControlAsync(fixture.Camera, fixture.Bias());
            await biasEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task capture = connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("waiting-capture"));
            try
            {
                await connector.HandleCameraControlAsync(fixture.Camera,
                    fixture.Abort(CameraControlOps.CaptureAbort, "waiting-capture")).WaitAsync(TimeSpan.FromSeconds(2));
                await capture.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(bias.IsCompleted);
                Assert.Equal(0, temperatureReads);
                Assert.False(Assert.Single(fixture.Captures).IsSuccess);
                Assert.Empty(fixture.Store.Query());
            }
            finally
            {
                releaseBias.TrySetResult();
                await Task.WhenAll(bias, capture).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task LateCaptureAbortForOldRequest_DoesNotCancelNewCapture()
        {
            using var fixture = new Fixture();
            await fixture.Manager.StartAllAsync();
            var captureEntered = Signal();
            var releaseCapture = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var connector = fixture.Create((_, _, _) => Task.CompletedTask,
                _ => { captureEntered.TrySetResult(); return releaseCapture.Task; });

            Task capture = connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("new-capture"));
            await captureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await connector.HandleCameraControlAsync(fixture.Camera,
                    fixture.Abort(CameraControlOps.CaptureAbort, "old-capture")).WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                releaseCapture.TrySetResult(25);
                await capture.WaitAsync(TimeSpan.FromSeconds(5));
            }

            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.Equal("new-capture", result.RecipeStepId);
            Assert.True(result.IsSuccess);
            Assert.Single(fixture.Store.Query());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("{\"CINT\":\"92\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"10\",\"GFID\":\"AE\"}")]
        public async Task ChangedOrResetPanelBias_RejectsPreviouslyVerifiedFile(string? currentJson)
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create((_, _, _) => Task.CompletedTask);
            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Bias());
            CameraControlAckMessage bias = Assert.Single(fixture.Acks);
            Assert.True(bias.IsSuccess);
            Assert.Equal(ValidBias, File.ReadAllText(bias.BiasFilePath));

            fixture.CurrentJson = currentJson;
            CaptureCommandMessage capture = fixture.Capture("capture-request");
            capture.StorageRootUnc = fixture.Buffer;
            capture.ConditionFolder = "LNN30";
            capture.BlackBodyFolder = "cold";
            capture.FilePrefix = "BB10";
            capture.WriteBiasJson = true;
            capture.RequiredBiasRequestId = "bias-request";
            await connector.HandleCaptureAsync(fixture.Camera, capture);

            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.False(result.IsSuccess);
            Assert.Equal(BiasExecutionPolicy.FileFailureCode, result.FailureCode);
            Assert.Contains("NG", result.Message);
            Assert.Equal(ValidBias, File.ReadAllText(bias.BiasFilePath));
            Assert.Empty(fixture.Store.Query());
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.raw", SearchOption.AllDirectories));
        }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class Fixture : IDisposable
        {
            private readonly LiteDbCaptureIndex _index;
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "hcs_bias_lifecycle_" + Guid.NewGuid().ToString("N"));
            public string Buffer => Path.Combine(Root, "production");
            public CameraDescriptor Camera { get; } = new("cam", 0, "test") { CameraSerialNumber = "544112136" };
            public Mock<INatsCommunicationService> Nats { get; } = new();
            public ConcurrentQueue<CameraControlAckMessage> Acks { get; } = new();
            public ConcurrentQueue<CaptureResultMessage> Captures { get; } = new();
            public CameraRuntimeManager Manager { get; }
            public CaptureStore Store { get; }
            public string? CurrentJson { get; set; } = ValidBias;

            public Fixture()
            {
                Directory.CreateDirectory(Root);
                Manager = new CameraRuntimeManager(d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                Manager.Add(Camera);
                _index = new LiteDbCaptureIndex(Path.Combine(Root, "index.db"));
                Store = new CaptureStore(Root, _index);
                Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                    .Callback<CameraControlAckMessage>(Acks.Enqueue).Returns(Task.CompletedTask);
                Nats.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                    .Callback<CaptureResultMessage>(Captures.Enqueue).Returns(Task.CompletedTask);
            }

            public CameraNatsConnector Create(
                Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task> handler,
                Func<CameraDescriptor, Task<double?>>? readTemperature = null)
                => new(Nats.Object, Manager, Store, new[] { Camera },
                    readCameraTemperature: readTemperature, readBiasJson: _ => CurrentJson,
                    productionSink: new ProductionCaptureSink(Buffer), biasControlHandler: handler);

            public CameraControlMessage Bias()
                => new()
                {
                    AgentId = Camera.AgentId,
                    Op = CameraControlOps.BiasLow,
                    RequestId = "bias-request",
                    RequireBiasFile = true,
                    BiasAttemptTimeoutSeconds = 10,
                    BiasStorage = new CaptureCommandMessage
                    {
                        StorageRootUnc = Buffer,
                        ConditionFolder = "LNN30",
                        BlackBodyFolder = "cold",
                        FilePrefix = "BB10"
                    }
                };

            public CaptureCommandMessage Capture(string requestId)
                => new() { TargetAgentId = Camera.AgentId, RecipeStepId = requestId, Source = CaptureSource.Recipe, ShotCount = 1 };

            public CameraControlMessage Abort(string op, string requestId)
                => new() { AgentId = Camera.AgentId, Op = op, RequestId = "abort-" + requestId, TargetRequestId = requestId };

            public void Dispose()
            {
                Manager.Dispose();
                Store.Dispose();
                _index.Dispose();
                try { Directory.Delete(Root, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
