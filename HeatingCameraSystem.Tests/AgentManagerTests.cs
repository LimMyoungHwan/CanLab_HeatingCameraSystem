using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.Services;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Protocols.Simulation;
using LiteDB;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    // ── FakeCameraEnumerator ──────────────────────────────────────────────────

    public class FakeCameraEnumeratorTests
    {
        [Fact]
        public void Enumerate_ReturnsTwoFakeCameras()
        {
            using var enumerator = new FakeCameraEnumerator();
            var cameras = enumerator.Enumerate();

            Assert.Equal(2, cameras.Count);
            Assert.All(cameras, c => Assert.False(string.IsNullOrEmpty(c.HardwareId)));
        }

        [Fact]
        public void SimulateArrival_FiresChangedEvent()
        {
            using var enumerator = new FakeCameraEnumerator();
            PnpChange? received = null;
            enumerator.Changed += change => received = change;

            var cam = new DiscoveredCamera { HardwareId = "USB\\TEST\\001", FriendlyName = "Test", OpenCvIndex = 5 };
            enumerator.SimulateArrival(cam);

            Assert.NotNull(received);
            Assert.Equal(PnpChangeType.Arrival, received!.ChangeType);
            Assert.Equal("USB\\TEST\\001", received.Camera.HardwareId);
        }

        [Fact]
        public void SimulateRemoval_FiresChangedEvent()
        {
            using var enumerator = new FakeCameraEnumerator();
            PnpChange? received = null;
            enumerator.Changed += change => received = change;

            var cam = new DiscoveredCamera { HardwareId = "USB\\TEST\\002", FriendlyName = "Test2", OpenCvIndex = 3 };
            enumerator.SimulateRemoval(cam);

            Assert.NotNull(received);
            Assert.Equal(PnpChangeType.Removal, received!.ChangeType);
        }
    }

    // ── AgentSupervisor (플래그 분리 후 spawn 스킵 동작) ─────────────────────────
    // [SC-12 범위 2] Design Ref: §4.3 — SimulationMode 제거 후 spawn 스킵 조건 검증.
    // spawn 스킵은 이제 AgentExePath 파일 존재 여부로만 결정된다.

    public class AgentSupervisorSimTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly AgentSupervisor _supervisor;

        public AgentSupervisorSimTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hcs_sup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            // [SC-12 범위 2] SimulationMode → SimulateEnumeration + SimulateAgentMode 로 교체.
            // AgentExePath 는 존재하지 않는 경로 → spawn 스킵 → 기존 테스트 동작 유지.
            var settings = new ManagerSettings
            {
                SimulateEnumeration = true,
                SimulateAgentMode   = true,
                InstallRoot         = _tempDir,
                // AgentExePath 기본값이 존재하지 않는 경로이므로 spawn은 스킵됨
            };
            var store = new ManagerStateStore(_tempDir);
            _supervisor = new AgentSupervisor(settings, store, NullLogger<AgentSupervisor>.Instance);
        }

        [Fact]
        public void IsRunning_AfterSimSpawn_DoesNotThrow()
        {
            // exe 없음 → spawn 스킵 → Process 미시작 → IsRunning은 예외 없이 false 반환해야 함
            _supervisor.Spawn(new CameraEntry { HardwareId = "HW_SIM", AgentId = "PC_sim0001", OpenCvIndex = 0 });

            var ex = Record.Exception(() => _supervisor.IsRunning("HW_SIM"));

            Assert.Null(ex);
            Assert.False(_supervisor.IsRunning("HW_SIM"));
        }

        [Fact]
        public void Kill_AfterSimSpawn_DoesNotThrow()
        {
            // exe 없음 → spawn 스킵 → Kill 시 미시작 Process에 접근해도 예외 없어야 함
            _supervisor.Spawn(new CameraEntry { HardwareId = "HW_SIM2", AgentId = "PC_sim0002", OpenCvIndex = 1 });

            var ex = Record.Exception(() => _supervisor.Kill("HW_SIM2"));

            Assert.Null(ex);
        }

        public void Dispose()
        {
            _supervisor.Dispose();
            try { Directory.Delete(_tempDir, true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ── ManagerSettings 플래그 분리 검증 ─────────────────────────────────────────
    // [SC-12 범위 2] Plan SC: SC-04 — ManagerSettings JSON 왕복 직렬화 검증.

    public class ManagerSettingsFlagTests
    {
        [Fact]
        public void SimulateFlags_DefaultFalse()
        {
            // 기본값이 false인지 확인 — 실 운영 환경에서 시뮬레이션이 켜지면 안 됨
            var settings = new ManagerSettings();

            Assert.False(settings.SimulateEnumeration);
            Assert.False(settings.SimulateAgentMode);
        }

        [Fact]
        public void SimulateFlags_JsonRoundTrip_PreservesValues()
        {
            // JSON 직렬화 후 역직렬화해도 플래그 값이 유지되는지 확인
            var original = new ManagerSettings
            {
                SimulateEnumeration = true,
                SimulateAgentMode   = true,
            };

            var json     = System.Text.Json.JsonSerializer.Serialize(original);
            var restored = System.Text.Json.JsonSerializer.Deserialize<ManagerSettings>(json)!;

            Assert.True(restored.SimulateEnumeration);
            Assert.True(restored.SimulateAgentMode);
        }

        [Fact]
        public void SimulateFlags_CanBeSetIndependently()
        {
            // 두 플래그가 서로 독립적임을 확인 — 핵심 분리 설계 검증
            var onlyEnum = new ManagerSettings { SimulateEnumeration = true, SimulateAgentMode = false };
            var onlyMode = new ManagerSettings { SimulateEnumeration = false, SimulateAgentMode = true };

            Assert.True(onlyEnum.SimulateEnumeration);
            Assert.False(onlyEnum.SimulateAgentMode);

            Assert.False(onlyMode.SimulateEnumeration);
            Assert.True(onlyMode.SimulateAgentMode);
        }
    }

    // ── ManagerStateStore ─────────────────────────────────────────────────────

    public class ManagerStateStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ManagerStateStore _store;

        public ManagerStateStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"hcs_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _store = new ManagerStateStore(_tempDir);
        }

        [Fact]
        public void Upsert_And_GetByHardwareId_RoundTrips()
        {
            var entry = new CameraEntry
            {
                HardwareId = "USB\\VID_1234&PID_5678\\SN001",
                AgentId    = "PC1_abcd1234",
                Alias      = "Bay1-Top",
                IsApproved = true,
                FirstSeen  = DateTime.UtcNow,
                LastSeen   = DateTime.UtcNow,
            };

            _store.Upsert(entry);
            var loaded = _store.GetByHardwareId("USB\\VID_1234&PID_5678\\SN001");

            Assert.NotNull(loaded);
            Assert.Equal("Bay1-Top", loaded!.Alias);
            Assert.Equal("PC1_abcd1234", loaded.AgentId);
        }

        [Fact]
        public void Load_RestoresFromDisk()
        {
            _store.Upsert(new CameraEntry { HardwareId = "HW1", AgentId = "A1" });

            var store2 = new ManagerStateStore(_tempDir);
            store2.Load();
            var entry = store2.GetByHardwareId("HW1");

            Assert.NotNull(entry);
            Assert.Equal("A1", entry!.AgentId);
        }

        [Fact]
        public void Remove_DeletesEntry()
        {
            _store.Upsert(new CameraEntry { HardwareId = "HW_DEL", AgentId = "A_DEL" });
            _store.Remove("HW_DEL");

            Assert.Null(_store.GetByHardwareId("HW_DEL"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* best effort cleanup */ }
        }
    }

    // ── ManagerCommandHandler ─────────────────────────────────────────────────

    public class ManagerCommandHandlerTests
    {
        [Fact]
        public void BuildAgentId_ProducesDeterministicHash()
        {
            string id1 = ManagerCommandHandler.BuildAgentId("Bay1", "USB\\VID_1234&PID_5678\\SN001");
            string id2 = ManagerCommandHandler.BuildAgentId("Bay1", "USB\\VID_1234&PID_5678\\SN001");

            Assert.Equal(id1, id2);
            Assert.StartsWith("Bay1_", id1);
            Assert.Equal("Bay1_".Length + 8, id1.Length);
        }

        [Fact]
        public void BuildAgentId_DifferentHardwareId_DifferentHash()
        {
            string id1 = ManagerCommandHandler.BuildAgentId("PC1", "USB\\A");
            string id2 = ManagerCommandHandler.BuildAgentId("PC1", "USB\\B");

            Assert.NotEqual(id1, id2);
        }
    }

    // ── CameraDeviceRepository (LiteDB) ───────────────────────────────────────

    public class CameraDeviceRepositoryTests : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly LiteDbCameraDeviceRepository _repo;

        public CameraDeviceRepositoryTests()
        {
            _db = new LiteDatabase(":memory:");
            _repo = new LiteDbCameraDeviceRepository(_db);
        }

        [Fact]
        public async Task Upsert_And_GetByHardwareId_RoundTrips()
        {
            var device = new CameraDevice
            {
                HardwareId = "HW_TEST_1",
                AgentId    = "Agent_Test",
                Alias      = "TestCam",
                PCId       = "PC1",
                IsApproved = true,
            };

            await _repo.UpsertAsync(device);
            var loaded = await _repo.GetByHardwareIdAsync("HW_TEST_1");

            Assert.NotNull(loaded);
            Assert.Equal("TestCam", loaded!.Alias);
        }

        [Fact]
        public async Task GetByAlias_ReturnsCorrectDevice()
        {
            await _repo.UpsertAsync(new CameraDevice { HardwareId = "HW_A", Alias = "Bay1-Left" });
            await _repo.UpsertAsync(new CameraDevice { HardwareId = "HW_B", Alias = "Bay1-Right" });

            var found = await _repo.GetByAliasAsync("Bay1-Right");

            Assert.NotNull(found);
            Assert.Equal("HW_B", found!.HardwareId);
        }

        [Fact]
        public async Task GetByAlias_ReturnsNull_WhenNotFound()
        {
            var found = await _repo.GetByAliasAsync("NonExistent");
            Assert.Null(found);
        }

        [Fact]
        public async Task Delete_RemovesDevice()
        {
            await _repo.UpsertAsync(new CameraDevice { HardwareId = "HW_DEL" });
            await _repo.DeleteByHardwareIdAsync("HW_DEL");

            Assert.Null(await _repo.GetByHardwareIdAsync("HW_DEL"));
        }

        public void Dispose() => _db.Dispose();
    }

    // ── MigrationService ──────────────────────────────────────────────────────

    public class MigrationServiceTests : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly LiteDbCameraDeviceRepository _repo;

        public MigrationServiceTests()
        {
            _db = new LiteDatabase(":memory:");
            _repo = new LiteDbCameraDeviceRepository(_db);
        }

        [Fact]
        public async Task Run_MigratesOldSerialSettings()
        {
            var oldCol = _db.GetCollection<CameraSerialSettings>("CameraSerialSettings");
            oldCol.Insert(new CameraSerialSettings { CameraIndex = 0, PortName = "COM5", BaudRate = 115200 });
            oldCol.Insert(new CameraSerialSettings { CameraIndex = 1, PortName = "COM6", BaudRate = 9600 });

            MigrationService.Run(_db, _repo);

            var devices = (await _repo.GetAllAsync()).ToList();
            Assert.Equal(2, devices.Count);
            Assert.Contains(devices, d => d.HardwareId == "legacy_0" && d.SerialSettings.PortName == "COM5");
            Assert.Contains(devices, d => d.HardwareId == "legacy_1" && d.SerialSettings.BaudRate == 9600);

            Assert.Empty(oldCol.FindAll());
        }

        [Fact]
        public async Task Run_IsIdempotent()
        {
            var oldCol = _db.GetCollection<CameraSerialSettings>("CameraSerialSettings");
            oldCol.Insert(new CameraSerialSettings { CameraIndex = 0, PortName = "COM3" });

            MigrationService.Run(_db, _repo);
            MigrationService.Run(_db, _repo);

            var devices = (await _repo.GetAllAsync()).ToList();
            Assert.Single(devices);
        }

        public void Dispose() => _db.Dispose();
    }

    // ── RecipeEngine CameraAlias ──────────────────────────────────────────────

    public class RecipeEngineAliasTests
    {
        [Fact]
        public async Task ExecuteRecipe_WithAlias_UsesDeviceRepoAgentId()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            var mockNats = new Mock<INatsCommunicationService>();
            CaptureCommandMessage? captured = null;
            mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                .Callback<CaptureCommandMessage>(m => captured = m)
                .Returns(Task.CompletedTask);
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(cb =>
                {
                    // auto-respond with success when capture command is published
                    mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                        .Callback<CaptureCommandMessage>(m =>
                        {
                            captured = m;
                            cb(new CaptureResultMessage
                            {
                                AgentId      = m.TargetAgentId,
                                RecipeStepId = m.RecipeStepId,
                                IsSuccess    = true,
                                Timestamp    = DateTime.UtcNow,
                            });
                        })
                        .Returns(Task.CompletedTask);
                })
                .Returns(Task.CompletedTask);

            var mockHistory = new Mock<ICaptureHistoryRepository>();
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Returns(Task.CompletedTask);

            var mockDeviceRepo = new Mock<ICameraDeviceRepository>();
            mockDeviceRepo.Setup(r => r.GetByAliasAsync("Bay1-Top"))
                .ReturnsAsync(new CameraDevice { AgentId = "Bay1_abcd1234", Alias = "Bay1-Top" });

            var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object,
                deviceRepo: mockDeviceRepo.Object);

            var recipe = new Recipe
            {
                Name = "AliasTest",
                Steps = { new RecipeStep { CameraAlias = "Bay1-Top", CameraIndex = 99 } }
            };

            await engine.ExecuteRecipeAsync(recipe);

            Assert.NotNull(captured);
            Assert.Equal("Bay1_abcd1234", captured!.TargetAgentId);
        }

        [Fact]
        public async Task ExecuteRecipe_WithoutAlias_FallsBackToCameraIndex()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            var mockNats = new Mock<INatsCommunicationService>();
            CaptureCommandMessage? captured = null;
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(cb =>
                {
                    mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                        .Callback<CaptureCommandMessage>(m =>
                        {
                            captured = m;
                            cb(new CaptureResultMessage
                            {
                                AgentId      = m.TargetAgentId,
                                RecipeStepId = m.RecipeStepId,
                                IsSuccess    = true,
                                Timestamp    = DateTime.UtcNow,
                            });
                        })
                        .Returns(Task.CompletedTask);
                })
                .Returns(Task.CompletedTask);

            var mockHistory = new Mock<ICaptureHistoryRepository>();
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Returns(Task.CompletedTask);

            var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object);

            var recipe = new Recipe
            {
                Name = "FallbackTest",
                Steps = { new RecipeStep { CameraIndex = 3 } }
            };

            await engine.ExecuteRecipeAsync(recipe);

            Assert.NotNull(captured);
            Assert.Equal("Agent_3", captured!.TargetAgentId);
        }

        [Fact]
        public async Task ExecuteRecipe_AliasNotFound_FallsBackToCameraIndex()
        {
            var plc = new FakePlcController();
            await plc.ConnectAsync("any");

            var mockNats = new Mock<INatsCommunicationService>();
            CaptureCommandMessage? captured = null;
            mockNats.Setup(n => n.SubscribeCaptureResultAsync(It.IsAny<Action<CaptureResultMessage>>()))
                .Callback<Action<CaptureResultMessage>>(cb =>
                {
                    mockNats.Setup(n => n.PublishCaptureCommandAsync(It.IsAny<CaptureCommandMessage>()))
                        .Callback<CaptureCommandMessage>(m =>
                        {
                            captured = m;
                            cb(new CaptureResultMessage
                            {
                                AgentId      = m.TargetAgentId,
                                RecipeStepId = m.RecipeStepId,
                                IsSuccess    = true,
                                Timestamp    = DateTime.UtcNow,
                            });
                        })
                        .Returns(Task.CompletedTask);
                })
                .Returns(Task.CompletedTask);

            var mockHistory = new Mock<ICaptureHistoryRepository>();
            mockHistory.Setup(h => h.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Returns(Task.CompletedTask);

            var mockDeviceRepo = new Mock<ICameraDeviceRepository>();
            mockDeviceRepo.Setup(r => r.GetByAliasAsync("NonExistent"))
                .ReturnsAsync((CameraDevice?)null);

            var engine = new RecipeEngine(plc, mockNats.Object, mockHistory.Object,
                deviceRepo: mockDeviceRepo.Object);

            var recipe = new Recipe
            {
                Name = "MissingAliasTest",
                Steps = { new RecipeStep { CameraAlias = "NonExistent", CameraIndex = 7 } }
            };

            await engine.ExecuteRecipeAsync(recipe);

            Assert.NotNull(captured);
            Assert.Equal("Agent_7", captured!.TargetAgentId);
        }
    }
}
