using System;
using System.Collections.Generic;
using System.IO;
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
    public class CameraBiasConnectorTests
    {
        private const string ValidBias = "{\"CINT\":\"93\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"10\",\"GFID\":\"AE\"}";

        [Fact]
        public async Task BiasFourthAttempt_SendsOneSuccessAckOnlyAfterFinalFileIsReadable()
        {
            using var fixture = new Fixture();
            int calls = 0;
            bool filePresentAtAck = false;
            fixture.Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                .Callback<CameraControlAckMessage>(ack =>
                {
                    fixture.Acks.Add(ack);
                    filePresentAtAck = ack.IsSuccess && ack.BiasFileSaved
                        && File.Exists(ack.BiasFilePath) && File.ReadAllText(ack.BiasFilePath) == ValidBias;
                }).Returns(Task.CompletedTask);
            await using var connector = fixture.Create((_, _, _) =>
            {
                if (++calls < 4) throw new IOException("시리얼 응답 없음");
                fixture.CurrentJson = ValidBias;
                return Task.CompletedTask;
            });

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Command());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.Equal(4, calls);
            Assert.Equal(4, result.Attempts);
            Assert.True(result.IsSuccess);
            Assert.True(filePresentAtAck);
            Assert.Equal("bias-request", result.RequestId);
        }

        [Fact]
        public async Task BiasFileCannotBeWritten_FailsAfterFourAttempts_EvenWithOldResult()
        {
            using var fixture = new Fixture { CurrentJson = ValidBias };
            string blockedRoot = Path.Combine(fixture.Root, "file-not-directory");
            File.WriteAllText(blockedRoot, "occupied");
            int calls = 0;
            await using var connector = fixture.Create((_, _, _) => { calls++; return Task.CompletedTask; });
            CameraControlMessage command = fixture.Command(blockedRoot);

            await connector.HandleCameraControlAsync(fixture.Camera, command);

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.Equal(4, calls);
            Assert.Equal(4, result.Attempts);
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
            Assert.Empty(result.BiasFilePath);
            Assert.Contains("NG", result.Message);
        }

        [Fact]
        public async Task RequiredBias_DoesNotFallBackToUnverifiedLegacyHandler()
        {
            using var fixture = new Fixture { CurrentJson = ValidBias };
            int legacyCalls = 0;
            await using var connector = new CameraNatsConnector(fixture.Nats.Object, fixture.Manager, fixture.Store,
                new[] { fixture.Camera },
                cameraControlHandler: (_, _) => { legacyCalls++; return Task.FromResult((true, "ok")); },
                readBiasJson: _ => fixture.CurrentJson,
                productionSink: new ProductionCaptureSink(fixture.Buffer));

            await connector.HandleCameraControlAsync(fixture.Camera, fixture.Command());

            CameraControlAckMessage result = Assert.Single(fixture.Acks);
            Assert.Equal(0, legacyCalls);
            Assert.Equal(4, result.Attempts);
            Assert.False(result.IsSuccess);
            Assert.False(result.BiasFileSaved);
        }

        [Fact]
        public async Task ColdRecipeWithoutMatchingBias_FailsBeforeTakingAnyPictures()
        {
            using var fixture = new Fixture { CurrentJson = ValidBias };
            await using var connector = fixture.Create((_, _, _) => Task.CompletedTask);
            var capture = new CaptureCommandMessage
            {
                TargetAgentId = fixture.Camera.AgentId,
                RecipeStepId = "capture-request",
                Source = CaptureSource.Recipe,
                StorageRootUnc = fixture.Buffer,
                ConditionFolder = "LNN30",
                BlackBodyFolder = "cold",
                FilePrefix = "BB10",
                ShotCount = 100,
                WriteBiasJson = true,
                RequiredBiasRequestId = "unknown-bias-request"
            };

            await connector.HandleCaptureAsync(fixture.Camera, capture);

            CaptureResultMessage result = Assert.Single(fixture.Captures);
            Assert.False(result.IsSuccess);
            Assert.Equal(BiasExecutionPolicy.FileFailureCode, result.FailureCode);
            Assert.Contains("NG", result.Message);
            Assert.Empty(fixture.Store.Query());
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.raw", SearchOption.AllDirectories));
        }

        private sealed class Fixture : IDisposable
        {
            private readonly LiteDbCaptureIndex _index;
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "hcs_bias_connector_" + Guid.NewGuid().ToString("N"));
            public string Buffer => Path.Combine(Root, "production");
            public CameraDescriptor Camera { get; } = new("cam", 0, "test") { CameraSerialNumber = "544112136" };
            public Mock<INatsCommunicationService> Nats { get; } = new();
            public List<CameraControlAckMessage> Acks { get; } = new();
            public List<CaptureResultMessage> Captures { get; } = new();
            public CameraRuntimeManager Manager { get; }
            public CaptureStore Store { get; }
            public string? CurrentJson { get; set; }

            public Fixture()
            {
                Directory.CreateDirectory(Root);
                Manager = new CameraRuntimeManager(d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()));
                _index = new LiteDbCaptureIndex(Path.Combine(Root, "index.db"));
                Store = new CaptureStore(Root, _index);
                Nats.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                    .Callback<CameraControlAckMessage>(Acks.Add).Returns(Task.CompletedTask);
                Nats.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                    .Callback<CaptureResultMessage>(Captures.Add).Returns(Task.CompletedTask);
            }

            public CameraNatsConnector Create(Func<CameraDescriptor, CameraControlMessage, CancellationToken, Task> handler)
                => new(Nats.Object, Manager, Store, new[] { Camera }, readBiasJson: _ => CurrentJson,
                    productionSink: new ProductionCaptureSink(Buffer), biasControlHandler: handler);

            public CameraControlMessage Command(string? root = null)
                => new()
                {
                    AgentId = Camera.AgentId,
                    CameraIndex = 0,
                    Op = CameraControlOps.BiasLow,
                    BiasTargetLevel = 8500,
                    RequestId = "bias-request",
                    RequireBiasFile = true,
                    BiasAttemptTimeoutSeconds = 1,
                    BiasStorage = new CaptureCommandMessage
                    {
                        StorageRootUnc = root ?? Buffer,
                        ConditionFolder = "LNN30",
                        BlackBodyFolder = "cold",
                        FilePrefix = "BB10",
                        WriteBiasJson = true
                    }
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
