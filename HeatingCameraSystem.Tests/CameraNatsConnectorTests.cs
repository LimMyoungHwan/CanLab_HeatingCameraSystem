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
    public class CameraNatsConnectorTests
    {
        private const string ValidBiasJson = "{\"CINT\":\"93\",\"TINT\":\"273\",\"GSK_MSB\":1,\"GSK_LSB\":\"80\",\"GFID\":\"AE\"}";

        [Fact]
        public async Task HandleCapture_TeesSnapshot_Persists_And_PublishesResult()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0");
                manager.Add(descriptor);
                await manager.StartAllAsync();

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor });

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    RecipeStepId = "s1",
                    Timestamp = DateTime.UtcNow
                });

                Assert.NotNull(published);
                Assert.True(published!.IsSuccess);
                Assert.Equal("cam0", published.AgentId);
                Assert.Equal("s1", published.RecipeStepId);
                Assert.Equal("Camera 0", published.Alias);
                Assert.Equal(0, published.CameraIndex);
                Assert.Equal(CaptureSource.Recipe, published.Source);
                Assert.False(string.IsNullOrWhiteSpace(published.CaptureId));
                Assert.NotNull(published.ImageBytes);
                Assert.True(published.ImageBytes!.Length > 0);
                Assert.True(File.Exists(published.ImagePath));
                Assert.Empty(published.ProductionRawPath);

                Assert.Single(store.Query());

                await manager.StopAllAsync();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCapture_UnknownCamera_PublishesFailure()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                var descriptor = new CameraDescriptor("ghost", 7, "Ghost");
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor });

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "ghost",
                    RecipeStepId = "s1",
                    Timestamp = DateTime.UtcNow
                });

                Assert.NotNull(published);
                Assert.False(published!.IsSuccess);
                Assert.Equal("ghost", published.AgentId);
                Assert.Null(published.ImageBytes);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCameraControl_RoutesToHandler_AndPublishesAck()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_control_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CameraControlAckMessage? published = null;
                natsMock.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                        .Callback<CameraControlAckMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0");
                CameraDescriptor? handledCamera = null;
                string? handledOp = null;
                double handledBiasTarget = 0;

                await using (var connector = new CameraNatsConnector(
                    natsMock.Object,
                    manager,
                    store,
                    new[] { descriptor },
                    cameraControlHandler: (camera, message) =>
                    {
                        handledCamera = camera;
                        handledOp = message.Op;
                        handledBiasTarget = message.BiasTargetLevel;
                        return Task.FromResult((Success: true, Message: "ok"));
                    }))
                {
                    await connector.HandleCameraControlAsync(descriptor, new CameraControlMessage
                    {
                        AgentId = descriptor.AgentId,
                        CameraIndex = descriptor.OpenCvIndex,
                        Op = CameraControlOps.Run,
                        RequestId = "request-1",
                        BiasTargetLevel = 8200,
                        Timestamp = DateTime.UtcNow
                    });
                }

                Assert.Same(descriptor, handledCamera);
                Assert.Equal(CameraControlOps.Run, handledOp);
                Assert.Equal(8200, handledBiasTarget);
                Assert.NotNull(published);
                Assert.Equal(descriptor.AgentId, published!.AgentId);
                Assert.Equal(CameraControlOps.Run, published.Op);
                Assert.Equal("request-1", published.RequestId);
                Assert.True(published.IsSuccess);
                Assert.Equal("ok", published.Message);
                natsMock.Verify(n => n.PublishCameraControlAckAsync(It.Is<CameraControlAckMessage>(m =>
                    m.AgentId == descriptor.AgentId &&
                    m.Op == CameraControlOps.Run &&
                    m.IsSuccess)), Times.Once);

                published = null;
                await using (var connectorWithoutHandler = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor }))
                {
                    await connectorWithoutHandler.HandleCameraControlAsync(descriptor, new CameraControlMessage
                    {
                        AgentId = descriptor.AgentId,
                        CameraIndex = descriptor.OpenCvIndex,
                        Op = CameraControlOps.Stop,
                        Timestamp = DateTime.UtcNow
                    });
                }

                Assert.NotNull(published);
                Assert.False(published!.IsSuccess);
                Assert.Equal("control handler not wired", published.Message);
                natsMock.Verify(n => n.PublishCameraControlAckAsync(It.Is<CameraControlAckMessage>(m =>
                    m.AgentId == descriptor.AgentId &&
                    m.Op == CameraControlOps.Stop &&
                    !m.IsSuccess)), Times.Once);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task SyncSubscriptions_AddsOnlyNewCameraSubscriptions()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_nats_sync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                var cameras = new List<CameraDescriptor>
                {
                    new("cam0", 0, "Camera 0")
                };
                var subscribed = new List<string>();
                natsMock.Setup(n => n.ConnectAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
                natsMock.Setup(n => n.SubscribeCaptureCommandAsync(It.IsAny<string>(), It.IsAny<Action<CaptureCommandMessage>>()))
                    .Callback<string, Action<CaptureCommandMessage>>((agentId, _) => subscribed.Add($"capture:{agentId}"))
                    .Returns(Task.CompletedTask);
                natsMock.Setup(n => n.SubscribeCameraControlAsync(It.IsAny<string>(), It.IsAny<Action<CameraControlMessage>>()))
                    .Callback<string, Action<CameraControlMessage>>((agentId, _) => subscribed.Add($"control:{agentId}"))
                    .Returns(Task.CompletedTask);

                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(natsMock.Object, manager, store, cameras);

                connector.Start("nats://127.0.0.1:4222");
                await WaitUntilAsync(() => subscribed.Count == 2);

                cameras.Add(new CameraDescriptor("cam1", 1, "Camera 1"));
                await connector.SyncSubscriptionsAsync();
                await connector.SyncSubscriptionsAsync();

                Assert.Equal(4, subscribed.Count);
                Assert.Single(subscribed.FindAll(s => s == "capture:cam1"));
                Assert.Single(subscribed.FindAll(s => s == "control:cam1"));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCapture_ProductionRaw_StaysNucUncorrected()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_prodraw_" + Guid.NewGuid().ToString("N"));
            string buffer = Path.Combine(dir, "buffer");
            string storage = Path.Combine(dir, "storage");
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                bool? rawExistedWhenPublished = null;
                byte[]? rawBytesWhenPublished = null;
                string? biasJsonWhenPublished = null;
                string biasPath = Path.Combine(storage, "S1_P1", "RPP40", "bias.json");
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m =>
                        {
                            published = m;
                            rawExistedWhenPublished = File.Exists(m.ProductionRawPath);
                            if (rawExistedWhenPublished == true)
                                rawBytesWhenPublished = File.ReadAllBytes(m.ProductionRawPath);
                            if (File.Exists(biasPath))
                                biasJsonWhenPublished = File.ReadAllText(biasPath);
                        })
                        .Returns(Task.CompletedTask);

                ThermalFrame source = GradientFrame();
                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FixedFrameSource(source), framePeriodMs: 10));
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0", CameraSerialNumber: "S1");
                manager.Add(descriptor);
                await manager.StartAllAsync();

                // 소스 프레임 자체를 flat-field 기준으로 잡으면 보정 결과가 균일해져 원본과 확실히 달라진다.
                var nuc = new ThermalNucCorrector();
                nuc.CaptureFromFlat(source);

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor },
                    nucs: new Dictionary<string, ThermalNucCorrector> { ["cam0"] = nuc },
                    readBiasJson: _ => ValidBiasJson,
                    productionSink: new ProductionCaptureSink(buffer));

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    StorageRootUnc = storage,
                    ProductNumber = "P1",
                    ConditionFolder = "RPP40",
                    BlackBodyFolder = "cold",
                    FilePrefix = "BB20",
                    ShotCount = 3,
                    ShotNumberOffset = 17,
                    WriteBiasJson = true
                });

                string raw = Path.Combine(storage, "S1_P1", "RPP40", "cold", "BB20_019.raw");
                Assert.True(File.Exists(raw));
                Assert.NotNull(published);
                Assert.True(published.IsSuccess);
                Assert.Equal(raw, published.ProductionRawPath);
                // 생산 결과는 최종 폴더 동기화가 끝난 뒤 발행한다. 그 시점의 원본도 이미 읽을 수 있어야 한다.
                Assert.Equal(true, rawExistedWhenPublished);
                natsMock.Verify(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()), Times.Once);
                Assert.Single(store.Query());

                var expected = new byte[source.Pixels.Length * sizeof(ushort)];
                Buffer.BlockCopy(source.Pixels, 0, expected, 0, expected.Length);
                Assert.Equal(expected, File.ReadAllBytes(raw));
                Assert.NotNull(rawBytesWhenPublished);
                Assert.Equal(expected, rawBytesWhenPublished);
                string rawDirectory = Path.Combine(storage, "S1_P1", "RPP40", "cold");
                Assert.Equal(3, Directory.GetFiles(rawDirectory, "*.raw").Length);
                for (int shotNumber = 17; shotNumber <= 19; shotNumber++)
                    Assert.Equal(expected, File.ReadAllBytes(Path.Combine(rawDirectory, $"BB20_{shotNumber:000}.raw")));
                Assert.Equal(ValidBiasJson, biasJsonWhenPublished);
                Assert.Equal(ValidBiasJson, File.ReadAllText(biasPath));

                await manager.StopAllAsync();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCapture_ProductionJpeg_IsEncodedFromNucCorrectedFrame()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_prodjpg_" + Guid.NewGuid().ToString("N"));
            string buffer = Path.Combine(dir, "buffer");
            string storage = Path.Combine(dir, "storage");
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(m => published = m)
                        .Returns(Task.CompletedTask);

                ThermalFrame source = GradientFrame();
                using var manager = new CameraRuntimeManager(
                    d => new CameraRuntime(d.OpenCvIndex, new FixedFrameSource(source), framePeriodMs: 10));
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0", CameraSerialNumber: "S1");
                manager.Add(descriptor);
                await manager.StartAllAsync();

                // 소스와 무관한 저진폭 flat(±10, dead 픽셀 임계 50 미만)이라 보정 프레임이 균일해지지
                // 않는다 — 균일하면 전부 검정이 되어 비교가 무의미해진다.
                var flat = new ushort[64 * 64];
                for (int i = 0; i < flat.Length; i++) flat[i] = (ushort)(i % 2 == 0 ? 2000 : 2020);
                var nuc = new ThermalNucCorrector();
                nuc.CaptureFromFlat(new ThermalFrame(flat, 64, 64, DateTimeOffset.Now));

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor },
                    nucs: new Dictionary<string, ThermalNucCorrector> { ["cam0"] = nuc },
                    readBiasJson: _ => ValidBiasJson,
                    productionSink: new ProductionCaptureSink(buffer));

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    StorageRootUnc = storage,
                    ProductNumber = "P1",
                    ConditionFolder = "RPP40",
                    BlackBodyFolder = "cold",
                    FilePrefix = "BB20",
                    ShotCount = 1,
                    WriteBiasJson = true,
                    SaveFormat = ProductionCaptureFormat.Jpeg
                });

                string jpeg = Path.Combine(storage, "S1_P1", "RPP40", "cold", "BB20_000.jpg");
                Assert.True(File.Exists(jpeg));
                Assert.NotNull(published);
                Assert.Empty(published.ProductionRawPath);

                ThermalFrame corrected = nuc.Apply(source);
                Assert.True(corrected.Pixels.Distinct().Count() > 1);

                byte[] written = File.ReadAllBytes(jpeg);
                Assert.Equal(ThermalPreviewEncoder.EncodeJpeg(corrected), written);
                Assert.NotEqual(ThermalPreviewEncoder.EncodeJpeg(source), written);
                Assert.Equal(ValidBiasJson, File.ReadAllText(Path.Combine(storage, "S1_P1", "RPP40", "bias.json")));

                await manager.StopAllAsync();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task HandleCapture_Cold_UsesBiasSnapshotEvenIfCacheClearsDuringCapture()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_coldbias_" + Guid.NewGuid().ToString("N"));
            string buffer = Path.Combine(dir, "buffer");
            string storage = Path.Combine(dir, "storage");
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                var results = new List<CaptureResultMessage>();
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(results.Add)
                        .Returns(Task.CompletedTask);

                string? cachedBias = ValidBiasJson;
                int biasReads = 0;
                var runtime = new Mock<ICameraRuntime>();
                runtime.Setup(r => r.StopAsync()).Returns(Task.CompletedTask);
                runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                       .Callback(() => cachedBias = null)
                       .ReturnsAsync(GradientFrame());
                using var manager = new CameraRuntimeManager(_ => runtime.Object);
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0", CameraSerialNumber: "S1");
                manager.Add(descriptor);

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor },
                    readBiasJson: _ => { biasReads++; return cachedBias; },
                    productionSink: new ProductionCaptureSink(buffer));

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    StorageRootUnc = storage,
                    ProductNumber = "P1",
                    ConditionFolder = "RPP40",
                    BlackBodyFolder = "cold",
                    FilePrefix = "BB20",
                    ShotCount = 1,
                    WriteBiasJson = true
                });

                Assert.True(Assert.Single(results).IsSuccess);
                Assert.Equal(1, biasReads);
                Assert.Null(cachedBias);
                Assert.Equal(ValidBiasJson, File.ReadAllText(Path.Combine(storage, "S1_P1", "RPP40", "bias.json")));
                Assert.True(File.Exists(Path.Combine(storage, "S1_P1", "RPP40", "cold", "BB20_000.raw")));
                Assert.Single(store.Query());
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(" \r\n", true)]
        [InlineData("{invalid", true)]
        [InlineData("null", true)]
        [InlineData("[]", true)]
        public async Task HandleCapture_ColdWithoutValidBias_ReportsNgBeforeCaptureAndPreservesExistingFiles(string? cachedBias, bool hasHistoricalJson)
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_coldng_" + Guid.NewGuid().ToString("N"));
            string buffer = Path.Combine(dir, "buffer");
            string storage = Path.Combine(dir, "storage");
            string bufferCondition = Path.Combine(buffer, "S1_P1", "RPP40");
            string storageCondition = Path.Combine(storage, "S1_P1", "RPP40");
            Directory.CreateDirectory(Path.Combine(bufferCondition, "cold"));
            Directory.CreateDirectory(Path.Combine(storageCondition, "cold"));
            string bufferedRaw = Path.Combine(bufferCondition, "cold", "BB20_099.raw");
            string historicRaw = Path.Combine(storageCondition, "cold", "BB20_000.raw");
            string bufferedBias = Path.Combine(bufferCondition, "bias.json");
            string historicBias = Path.Combine(storageCondition, "bias.json");
            File.WriteAllBytes(bufferedRaw, new byte[] { 7, 0 });
            File.WriteAllBytes(historicRaw, new byte[] { 8, 0 });
            const string bufferedOldBias = "{\"GSK_LSB\":\"55\"}";
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                var results = new List<CaptureResultMessage>();
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                        .Callback<CaptureResultMessage>(results.Add)
                        .Returns(Task.CompletedTask);
                CameraControlAckMessage? proofAck = null;
                natsMock.Setup(n => n.PublishCameraControlAckAsync(It.IsAny<CameraControlAckMessage>()))
                        .Callback<CameraControlAckMessage>(ack => proofAck = ack)
                        .Returns(Task.CompletedTask);
                var runtime = new Mock<ICameraRuntime>();
                runtime.Setup(r => r.StopAsync()).Returns(Task.CompletedTask);
                runtime.Setup(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(GradientFrame());
                using var manager = new CameraRuntimeManager(_ => runtime.Object);
                var descriptor = new CameraDescriptor("cam0", 0, "Camera 0", CameraSerialNumber: "S1");
                manager.Add(descriptor);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                var sink = new ProductionCaptureSink(buffer);
                var warnings = new List<string>();
                var diagnostics = new List<string>();
                string? currentBias = ValidBiasJson;
                int calibrationCalls = 0;
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor },
                    readBiasJson: _ => currentBias,
                    productionSink: sink,
                    reportCaptureWarning: (camera, warning) =>
                    {
                        Assert.Equal(descriptor.AgentId, camera.AgentId);
                        warnings.Add(warning);
                    },
                    biasControlHandler: (_, _, _) => { calibrationCalls++; return Task.CompletedTask; },
                    diagnosticLog: diagnostics.Add);

                // 요청 식별자 누락만으로 실패시키지 않는다. 실제 성공 증빙을 만든 뒤 현재 결과를
                // 무효화하여 같은 레시피 요청의 과거 파일도 재사용/전송하지 않는지 확인한다.
                const string biasRequestId = "cold-bias-proof";
                await connector.HandleCameraControlAsync(descriptor, new CameraControlMessage
                {
                    AgentId = descriptor.AgentId,
                    Op = CameraControlOps.BiasLow,
                    RequestId = biasRequestId,
                    RequireBiasFile = true,
                    BiasAttemptTimeoutSeconds = 10,
                    BiasStorage = new CaptureCommandMessage
                    {
                        StorageRootUnc = storage,
                        ProductNumber = "P1",
                        ConditionFolder = "RPP40",
                        BlackBodyFolder = "cold",
                        FilePrefix = "BB20"
                    }
                });
                CameraControlAckMessage proof = Assert.IsType<CameraControlAckMessage>(proofAck);
                Assert.True(proof.IsSuccess);
                Assert.True(proof.BiasFileSaved);
                Assert.Equal(biasRequestId, proof.RequestId);
                Assert.Equal(historicBias, proof.BiasFilePath);
                Assert.Equal(ValidBiasJson, File.ReadAllText(historicBias));
                if (hasHistoricalJson)
                {
                    File.WriteAllText(bufferedBias, bufferedOldBias);
                }
                else
                {
                    File.Delete(historicBias);
                }
                currentBias = cachedBias;
                diagnostics.Clear();

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam0",
                    RecipeStepId = "cold-step",
                    Source = CaptureSource.Recipe,
                    StorageRootUnc = storage,
                    ProductNumber = "P1",
                    ConditionFolder = "RPP40",
                    BlackBodyFolder = "cold",
                    FilePrefix = "BB20",
                    ShotCount = 3,
                    WriteBiasJson = true,
                    RequiredBiasRequestId = biasRequestId
                });

                CaptureResultMessage result = Assert.Single(results);
                Assert.False(result.IsSuccess);
                Assert.Equal("cold-step", result.RecipeStepId);
                Assert.Equal(CaptureSource.Recipe, result.Source);
                Assert.Equal(BiasExecutionPolicy.FileFailureCode, result.FailureCode);
                Assert.Contains("NG", result.Message);
                Assert.Empty(result.ImagePath);
                Assert.Empty(result.ProductionRawPath);
                Assert.Null(result.ImageBytes);
                Assert.Empty(store.Query());
                runtime.Verify(r => r.CaptureSnapshotAsync(It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.Equal(BiasExecutionPolicy.MaxAttempts,
                    diagnostics.FindAll(message => message.StartsWith("[BIAS] START", StringComparison.Ordinal)).Count);
                Assert.Equal(1, calibrationCalls); // 촬영 실패 처리 중 보정을 다시 실행하지 않는다.
                string warning = Assert.Single(warnings);
                Assert.Contains("NG", warning);
                Assert.Equal(result.Message, warning);
                Assert.Equal(hasHistoricalJson ? 2 : 1, sink.PendingFiles);
                Assert.Equal(new byte[] { 7, 0 }, File.ReadAllBytes(bufferedRaw));
                Assert.Equal(new byte[] { 8, 0 }, File.ReadAllBytes(historicRaw));
                Assert.Equal(new[] { bufferedRaw }, Directory.GetFiles(bufferCondition, "*.raw", SearchOption.AllDirectories));
                Assert.Equal(new[] { historicRaw }, Directory.GetFiles(storageCondition, "*.raw", SearchOption.AllDirectories));
                Assert.False(File.Exists(Path.Combine(storageCondition, "cold", "BB20_099.raw")));
                Assert.False(File.Exists(Path.Combine(storageCondition, "cold", "BB20_002.raw")));
                Assert.Empty(Directory.GetFiles(dir, "*.y16", SearchOption.AllDirectories));
                if (hasHistoricalJson)
                {
                    Assert.Equal(bufferedOldBias, File.ReadAllText(bufferedBias));
                    Assert.Equal(ValidBiasJson, File.ReadAllText(historicBias)); // Old buffered JSON was not transferred.
                }
                else
                {
                    Assert.False(File.Exists(bufferedBias));
                    Assert.False(File.Exists(historicBias));
                }
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Theory]
        [InlineData(17, 18)]
        [InlineData(-9, 1)]
        public async Task HandleCapture_ProductionRaw_FailedWritePublishesLastAttemptedShotPath(
            int offset, int expectedShotNumber)
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_prodraw_failed_" + Guid.NewGuid().ToString("N"));
            string storage = Path.Combine(dir, "storage");
            Directory.CreateDirectory(dir);
            try
            {
                var natsMock = new Mock<INatsCommunicationService>();
                CaptureResultMessage? published = null;
                natsMock.Setup(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                    .Callback<CaptureResultMessage>(m => published = m)
                    .Returns(Task.CompletedTask);

                ThermalFrame nonRadiometric = GradientFrame() with { Bgr24 = new byte[64 * 64 * 3] };
                var runtime = new Mock<ICameraRuntime>();
                runtime.SetupSequence(r => r.CaptureSnapshotAsync(
                        It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(nonRadiometric)
                    .ReturnsAsync(nonRadiometric)
                    .ReturnsAsync((ThermalFrame?)null);
                using var manager = new CameraRuntimeManager(_ => runtime.Object);
                var descriptor = new CameraDescriptor("cam7", 7, "Camera 7", CameraSerialNumber: "S17");
                manager.Add(descriptor);
                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                await using var connector = new CameraNatsConnector(
                    natsMock.Object, manager, store, new[] { descriptor },
                    productionSink: new ProductionCaptureSink(Path.Combine(dir, "buffer")));

                await connector.HandleCaptureAsync(descriptor, new CaptureCommandMessage
                {
                    TargetAgentId = "cam7",
                    StorageRootUnc = storage,
                    ProductNumber = "P42",
                    ConditionFolder = "RPP40",
                    BlackBodyFolder = "hot",
                    FilePrefix = "BB20",
                    ShotCount = 3,
                    ShotNumberOffset = offset
                });

                Assert.NotNull(published);
                Assert.False(published.IsSuccess);
                Assert.Equal(Path.Combine(storage, "S17_P42", "RPP40", "hot",
                    $"BB20_{expectedShotNumber:000}.raw"), published.ProductionRawPath);
                Assert.False(File.Exists(published.ProductionRawPath));
                Assert.True(File.Exists(published.ImagePath));
                Assert.Single(store.Query());
                natsMock.Verify(n => n.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()), Times.Once);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static ThermalFrame GradientFrame()
        {
            var px = new ushort[64 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = (ushort)(1000 + i % 64);
            return new ThermalFrame(px, 64, 64, DateTimeOffset.Now);
        }

        // 항상 같은 픽셀을 내되 타임스탬프만 새로 찍는다 — 캡처의 maxAge 검사에 걸리지 않게.
        private sealed class FixedFrameSource : IThermalFrameSource
        {
            private readonly ThermalFrame _frame;

            public FixedFrameSource(ThermalFrame frame) => _frame = frame;

            public void Open() { }

            public ThermalFrame? Read() => new(_frame.Pixels, _frame.Width, _frame.Height, DateTimeOffset.Now);

            public void Close() { }

            public void Dispose() { }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++)
            {
                await Task.Delay(10);
            }

            Assert.True(condition());
        }
    }
}
