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
    public class AppliedBiasConnectorTests
    {
        private const string ValidBias = "{\"CINT\":\"93\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"10\",\"GFID\":\"AE\"}";

        [Theory]
        [InlineData(BiasApplicationSource.Manual)]
        [InlineData(BiasApplicationSource.Automatic)]
        public async Task UseCurrent_AdoptsAppliedBiasWithoutAutomaticSearch_AndAcknowledgesReadableFile(BiasApplicationSource source)
        {
            using var fixture = new Fixture();
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied) with { Source = source };
            fixture.CurrentApplied = applied;
            bool fileVerifiedAtAck = false;
            fixture.Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                .Callback<CameraControlAckMessage>(ack =>
                {
                    fixture.Acks.Enqueue(ack);
                    fileVerifiedAtAck = ack.BiasFileSaved && File.Exists(ack.BiasFilePath)
                        && File.ReadAllText(ack.BiasFilePath) == applied.Json;
                }).Returns(Task.CompletedTask);
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.True(result.IsSuccess);
            Assert.True(fileVerifiedAtAck);
            Assert.Equal(CameraControlOps.BiasUseCurrent, result.Op);
            Assert.Equal(1, result.Attempts);
            Assert.Equal(applied.ApplicationId, result.BiasApplicationId);
            Assert.Equal(CameraControlOps.BiasLow, result.BiasSourceOperation);
            Assert.Equal(1, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Equal(0, fixture.LegacyCalls);
            Assert.Same(applied, fixture.CurrentApplied);
        }

        [Fact]
        public async Task UseCurrent_RejectsDifferentBandBeforeReadbackOrFileWrite()
        {
            using var fixture = new Fixture();
            fixture.CurrentApplied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied) with { Operation = CameraControlOps.BiasMid };
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
            Assert.Contains("대역", result.Message);
            Assert.Equal(0, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task UseCurrent_RequiresAppliedStateEvenWhenLegacyJsonExists(bool wireAppliedState)
        {
            using var fixture = new Fixture { CurrentApplied = null };
            await using var connector = fixture.Create(wireAppliedState: wireAppliedState);

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
            Assert.Equal(0, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Equal(0, fixture.LegacyCalls);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task UseCurrent_DoesNotFallBackToGenericControlHandler()
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create(wireBiasHandler: false);

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.False(result.IsSuccess);
            Assert.Equal(4, result.Attempts);
            Assert.Equal(0, fixture.LegacyCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData(false, CameraControlOps.BiasLow)]
        [InlineData(true, CameraControlOps.Bias)]
        public async Task UseCurrent_RequiresFileAndConcreteSourceBand(bool requireFile, string sourceOperation)
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create();
            CameraControlMessage command = fixture.UseCurrent();
            command.RequireBiasFile = requireFile;
            command.BiasSourceOperation = sourceOperation;

            await connector.HandleCameraControlAsync(fixture.Camera, command);

            Assert.False(Assert.Single(fixture.Acks).IsSuccess);
            Assert.Equal(0, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
        }

        [Fact]
        public async Task UseCurrent_RejectsAppliedStateAndPanelJsonMismatch()
        {
            using var fixture = new Fixture { CurrentJson = ValidBias.Replace("\"93\"", "\"92\"") };
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            Assert.False(Assert.Single(fixture.Acks).IsSuccess);
            Assert.Equal(0, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task Capture_RejectsNewApplicationGenerationEvenWhenJsonIsIdentical()
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create();
            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());
            CameraControlAckMessage proof = Assert.Single(fixture.Acks);
            Assert.True(proof.IsSuccess);
            fixture.CurrentApplied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied) with { ApplicationId = "new-application" };

            await connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("proof-request", "LNN30"));

            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.False(result.IsSuccess);
            Assert.Equal(BiasExecutionPolicy.FileFailureCode, result.FailureCode);
            Assert.Contains("NG", result.Message);
            Assert.Equal(ValidBias, File.ReadAllText(proof.BiasFilePath));
            Assert.Empty(fixture.Store.Query());
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.raw", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task UseCurrent_CreatesNewProofForAnotherConditionWithoutChangingApplication()
        {
            using var fixture = new Fixture();
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied);
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent("first-proof", "LNN30"));
            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent("second-proof", "LNP25"));

            CameraControlAckMessage[] results = fixture.Acks.ToArray();
            Assert.Equal(2, results.Length);
            Assert.All(results, result =>
            {
                Assert.True(result.IsSuccess);
                Assert.True(result.BiasFileSaved);
                Assert.Equal(applied.ApplicationId, result.BiasApplicationId);
                Assert.Equal(ValidBias, File.ReadAllText(result.BiasFilePath));
            });
            Assert.NotEqual(results[0].BiasFilePath, results[1].BiasFilePath);
            Assert.Equal("LNN30", Path.GetFileName(Path.GetDirectoryName(results[0].BiasFilePath)));
            Assert.Equal("LNP25", Path.GetFileName(Path.GetDirectoryName(results[1].BiasFilePath)));
            Assert.Equal(2, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Same(applied, fixture.CurrentApplied);
        }

        [Fact]
        public async Task Capture_CannotReuseProofFromAnotherCondition()
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create();
            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());
            Assert.True(Assert.Single(fixture.Acks).IsSuccess);

            await connector.HandleCaptureAsync(fixture.Camera, fixture.Capture("proof-request", "LNP25"));

            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.False(result.IsSuccess);
            Assert.Equal(BiasExecutionPolicy.FileFailureCode, result.FailureCode);
            Assert.Contains("저장 경로", result.Message);
            Assert.Empty(fixture.Store.Query());
        }

        [Fact]
        public async Task UseCurrent_FileFailureRetriesReadbackAndStorageFourTimesWithoutAutomaticSearch()
        {
            using var fixture = new Fixture();
            AppliedBiasResult applied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied);
            string blockedRoot = Path.Combine(fixture.Root, "file-not-directory");
            File.WriteAllText(blockedRoot, "occupied");
            CameraControlMessage command = fixture.UseCurrent(root: blockedRoot);
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, command);

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
            Assert.Equal(4, result.Attempts);
            Assert.Equal(4, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Empty(result.BiasApplicationId);
            Assert.Same(applied, fixture.CurrentApplied);
        }

        [Fact]
        public async Task UseCurrent_GenerationChangesDuringReadback_DoesNotAdoptReplacementOnRetry()
        {
            using var fixture = new Fixture();
            await using var connector = fixture.Create(onHandle: _ =>
            {
                fixture.CurrentApplied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied) with { ApplicationId = "reapplied" };
            });

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.UseCurrent());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.False(result.IsSuccess);
            Assert.Contains("세대", result.Message);
            Assert.Equal(1, fixture.ReadbackCalls);
            Assert.Equal(0, fixture.AutomaticCalls);
            Assert.Empty(Directory.GetFiles(fixture.Root, "bias.json", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task AutomaticBias_ReportsApplicationMetadataWhenStateReaderIsProvided()
        {
            using var fixture = new Fixture();
            fixture.CurrentApplied = Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied) with { Source = BiasApplicationSource.Automatic };
            CameraControlMessage command = fixture.UseCurrent();
            command.Op = CameraControlOps.BiasLow;
            command.BiasSourceOperation = string.Empty;
            await using var connector = fixture.Create();

            await connector.HandleCameraControlAsync(fixture.Camera, command);

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.True(result.IsSuccess);
            Assert.Equal(Assert.IsType<AppliedBiasResult>(fixture.CurrentApplied).ApplicationId, result.BiasApplicationId);
            Assert.Equal(CameraControlOps.BiasLow, result.BiasSourceOperation);
            Assert.Equal(1, fixture.AutomaticCalls);
            Assert.Equal(0, fixture.ReadbackCalls);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly LiteDbCaptureIndex _index;
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "hcs_applied_bias_" + Guid.NewGuid().ToString("N"));
            public string Buffer => Path.Combine(Root, "production");
            public CameraDescriptor Camera { get; } = new("cam", 0, "test") { CameraSerialNumber = "544112136" };
            public Mock<INatsCommunicationService> Nats { get; } = new();
            public ConcurrentQueue<CameraControlAckMessage> Acks { get; } = new();
            public ConcurrentQueue<CaptureResultMessage> Captures { get; } = new();
            public CameraRuntimeManager Manager { get; }
            public CaptureStore Store { get; }
            public string? CurrentJson { get; set; } = ValidBias;
            public AppliedBiasResult? CurrentApplied { get; set; } = new("application-1", CameraControlOps.BiasLow, 0x10,
                ValidBias, BiasApplicationSource.Manual, DateTimeOffset.UtcNow);
            public int ReadbackCalls { get; private set; }
            public int AutomaticCalls { get; private set; }
            public int LegacyCalls { get; private set; }

            public Fixture()
            {
                Directory.CreateDirectory(Root);
                Manager = new CameraRuntimeManager(d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()));
                _index = new LiteDbCaptureIndex(Path.Combine(Root, "index.db"));
                Store = new CaptureStore(Root, _index);
                Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                    .Callback<CameraControlAckMessage>(Acks.Enqueue).Returns(Task.CompletedTask);
                Nats.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                    .Callback<CaptureResultMessage>(Captures.Enqueue).Returns(Task.CompletedTask);
            }

            public CameraNatsConnector Create(bool wireAppliedState = true, bool wireBiasHandler = true,
                Action<CameraControlMessage>? onHandle = null)
            {
                Task HandleBias(CameraDescriptor _, CameraControlMessage command, CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    if (command.Op == CameraControlOps.BiasUseCurrent) ReadbackCalls++;
                    else AutomaticCalls++;
                    onHandle?.Invoke(command);
                    return Task.CompletedTask;
                }
                Func<CameraDescriptor, AppliedBiasResult?>? readApplied = wireAppliedState ? _ => CurrentApplied : null;
                Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task>? handler = wireBiasHandler ? HandleBias : null;
                return new CameraNatsConnector(Nats.Object, Manager, Store, new[] { Camera },
                    cameraControlHandler: (_, _) => { LegacyCalls++; return Task.FromResult((true, "legacy ok")); },
                    readBiasJson: _ => CurrentJson, productionSink: new ProductionCaptureSink(Buffer),
                    biasControlHandler: handler, readAppliedBias: readApplied);
            }

            public CameraControlMessage UseCurrent(string requestId = "proof-request", string condition = "LNN30", string? root = null)
                => new()
                {
                    AgentId = Camera.AgentId,
                    Op = CameraControlOps.BiasUseCurrent,
                    RequestId = requestId,
                    RequireBiasFile = true,
                    BiasSourceOperation = CameraControlOps.BiasLow,
                    BiasAttemptTimeoutSeconds = 10,
                    BiasStorage = new CaptureCommandMessage
                    {
                        StorageRootUnc = root ?? Buffer,
                        ConditionFolder = condition,
                        BlackBodyFolder = "cold",
                        FilePrefix = "BB10"
                    }
                };

            public CaptureCommandMessage Capture(string biasRequestId, string condition)
                => new()
                {
                    TargetAgentId = Camera.AgentId,
                    Source = CaptureSource.Recipe,
                    RecipeStepId = "capture-request",
                    StorageRootUnc = Buffer,
                    ConditionFolder = condition,
                    BlackBodyFolder = "cold",
                    FilePrefix = "BB10",
                    RequiredBiasRequestId = biasRequestId,
                    WriteBiasJson = true,
                    ShotCount = 1
                };

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
