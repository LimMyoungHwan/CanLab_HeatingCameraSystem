using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras.CL;
using HeatingCameraSystem.Protocols.Simulation;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// Master 전체가 공유하는 정적 서비스 로케이터 — 의도적으로 DI 컨테이너를 두지 않는다.
    /// 서비스를 추가할 때는 여기에 프로퍼티를 더하고 <see cref="Initialize"/>에
    /// SimulationMode 분기와 실제 하드웨어 분기를 각각 등록한다.
    /// 테스트가 이 정적 상태를 공유하므로 테스트 스위트는 병렬 실행하지 않는다.
    /// </summary>
    public static class AppServices
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static HardwareSettings Settings { get; private set; } = new();
        public static string ImageCacheDir { get; private set; } = string.Empty;
        public static LiteDatabase Db { get; private set; } = null!;
        public static IRecipeRepository RecipeRepo { get; private set; } = null!;
        public static IDashboardLayoutRepository DashboardLayoutRepo { get; private set; } = null!;
        public static ICaptureHistoryRepository HistoryRepo { get; private set; } = null!;
        public static IChamberHistoryRepository ChamberHistoryRepo { get; private set; } = null!;
        public static IAlarmHistoryRepository? AlarmHistoryRepo { get; private set; }
        public static ICameraSerialSettingsRepository CameraSerialSettingsRepo { get; private set; } = null!;
        public static ICameraDeviceRepository CameraDeviceRepo { get; private set; } = null!;
        public static AgentDirectory AgentDirectory { get; private set; } = null!;
        public static NatsCommunicationService? NatsService { get; private set; }
        public static IPlcController? PlcController { get; private set; }
        public static IBlackBodyController? BlackBodyController { get; private set; }
        public static RecipeEngine? RecipeEngine { get; private set; }
        public static ConnectionMonitorService? ConnectionMonitor { get; private set; }
        public static PlcStatusService? PlcStatus { get; private set; }
        public static ILiveThermalCamera? LiveThermalCamera { get; private set; }
        public static ICameraComPairingService? CameraPairingService { get; private set; }
        public static Func<string, ICameraSerialClient>? CameraSerialClientFactory { get; private set; }

        // 운영자 알림 팝업 seam — 의존성 없는 stateless 서비스라 기본 인스턴스로 항상 사용 가능(Initialize 불필요).
        public static IDialogService DialogService { get; } = new MessageBoxDialogService();

        // 챔버 이력 레코더 참조 유지 (PlcStatus.Updated 구독자 — GC 방지).
        private static ChamberHistoryRecorder? _chamberRecorder;
        private static CaptureResultHistoryRecorder? _captureRecorder;

        // 종료 1회 보장 가드 — DisposeAsync 재진입 시 재-종료 방지.
        private static bool _disposed;

        private static string _hardwareJsonPath = string.Empty;

        /// <summary>
        /// 설정 로드 → LiteDB 열기 → 저장소·마이그레이션 → 프로토콜·엔진·상태 서비스 순으로
        /// 조립한다. <c>App.OnStartup</c>에서 가장 먼저 호출되며, SimulationMode면 PLC·카메라를
        /// Fake 구현으로 대체한다. 네트워크 접속은 여기서 하지 않고 <see cref="TryConnectServicesAsync"/>가 맡는다.
        /// </summary>
        public static void Initialize()
        {
            _disposed = false;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeatingCameraSystem");
            Directory.CreateDirectory(dir);

            Settings = LoadOrCreateSettings(dir);
            _hardwareJsonPath = Path.Combine(dir, "hardware.json");
            ImageCacheDir = Path.Combine(dir, "ImageCache");
            Directory.CreateDirectory(ImageCacheDir);

            Db = new LiteDatabase(Path.Combine(dir, "data.db"));

            // 레시피는 이제 <dir>/recipe 아래 JSON 파일 하나씩으로 영속된다(백업은 <dir>/recipe bak).
            var fileRecipeRepo = new FileRecipeRepository(dir);
            try
            {
                // 레거시 LiteDB에서 1회만 시드한다. 영속 _migrations 마커로 추적하므로 운영자가
                // 나중에 삭제한 레시피가 이후 빈 폴더 기동에서 되살아나지 않는다.
                MigrationService.MigrateRecipesToFiles(Db, fileRecipeRepo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppServices] Recipe migration failed: {ex.Message}");
            }
            RecipeRepo = fileRecipeRepo;

            DashboardLayoutRepo = new LiteDbDashboardLayoutRepository(Db);
            HistoryRepo = new LiteDbCaptureHistoryRepository(Db);
            ChamberHistoryRepo = new LiteDbChamberHistoryRepository(Db);
            AlarmHistoryRepo = new LiteDbAlarmHistoryRepository(Db);
            CameraSerialSettingsRepo = new LiteDbCameraSerialSettingsRepository(Db);
            CameraDeviceRepo = new LiteDbCameraDeviceRepository(Db);

            string dbPath = Path.Combine(dir, "data.db");
            MigrationService.BackupDatabase(dbPath);
            MigrationService.Run(Db, CameraDeviceRepo);

            NatsService = new NatsCommunicationService();

            if (Settings.SimulationMode)
            {
                PlcController     = new FakePlcController();
                CameraSerialClientFactory = portName => new FakeCameraSerialClient(portName);
                LiveThermalCamera         = new FakeLiveThermalCamera();
                CameraPairingService      = new FakeCameraComPairingService();
                System.Diagnostics.Debug.WriteLine("[AppServices] SimulationMode=true -> using Fake PLC + Fake Camera/Pairing");
            }
            else
            {
                PlcController     = new PlcXgtClient(Settings.Plc);
                CameraSerialClientFactory = portName => new ClSerialCameraClient(portName);
                LiveThermalCamera         = new CltcLiveThermalCamera();
                var cameraEnumerator      = new WmiCameraEnumerator();
                var usbSerialEnumerator   = new WmiUsbSerialEnumerator();
                CameraPairingService      = new CameraComPairingService(
                    cameraEnumerator, usbSerialEnumerator, CameraSerialClientFactory, Settings);
            }

            BlackBodyController = CreateBlackBodyController(Settings, PlcController);

            AgentDirectory = new AgentDirectory();
            RecipeEngine = new RecipeEngine(PlcController, NatsService, HistoryRepo, Settings.RecipeEngine, ImageCacheDir, CameraDeviceRepo, BlackBodyController, AgentDirectory);
            ConnectionMonitor = new ConnectionMonitorService(PlcController, Settings);
            if (!Settings.SimulationMode) ConnectionMonitor.Start();

            PlcStatus = new PlcStatusService(PlcController, Settings.BlackBody.Enabled ? BlackBodyController : null);
            PlcStatus.ErrorRaised += (_, _) => RecipeEngine?.RequestEmergencyStop();
            PlcStatus.Start();

            _chamberRecorder = new ChamberHistoryRecorder(ChamberHistoryRepo, PlcStatus);
            _captureRecorder = new CaptureResultHistoryRecorder(HistoryRepo, ImageCacheDir, () => PlcStatus?.Snapshot);
        }

        /// <summary>흑체 컨트롤러를 만든다. SimulationMode면 Fake, 아니면 PLC 경유 SR 실물 구현이다.</summary>
        public static IBlackBodyController CreateBlackBodyController(HardwareSettings settings, IPlcController plc)
        {
            if (settings.SimulationMode) return new FakeBlackBodyController();
            return new SrBlackBodyController(settings.BlackBody, plc: plc);
        }

        /// <summary>
        /// NATS → PLC → 흑체 순으로 접속을 시도한다. 각 실패는 <see cref="AlarmSink"/> 경고로만
        /// 보고하고 다음 접속을 계속하므로 일부만 연결된 상태로도 앱은 뜬다.
        /// NATS 접속 성공 시 Agent 상태·캡처 결과 구독까지 여기서 건다.
        /// </summary>
        public static async Task TryConnectServicesAsync()
        {
            try
            {
                await NatsService!.ConnectAsync(Settings.Nats.Url);
                System.Diagnostics.Debug.WriteLine("[AppServices] NATS connected.");
                await NatsService.SubscribeAgentStatusAsync(msg => AgentDirectory.Note(msg));
                await NatsService.SubscribeCaptureResultAsync(msg => _ = _captureRecorder!.RecordAsync(msg));
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmSeverity.Warning, "NATS", $"연결 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AppServices] NATS connect failed: {ex.Message}");
            }

            try
            {
                await PlcController!.ConnectAsync(Settings.Plc.IpAddress, Settings.Plc.Port);
                System.Diagnostics.Debug.WriteLine("[AppServices] PLC connected.");
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmSeverity.Warning, "PLC", $"연결 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AppServices] PLC connect failed: {ex.Message}");
            }

            try
            {
                if (BlackBodyController != null) await BlackBodyController.ConnectAsync();
                System.Diagnostics.Debug.WriteLine("[AppServices] BlackBody connected.");
            }
            catch (Exception ex)
            {
                AlarmSink.Raise(AlarmSeverity.Warning, "흑체", $"연결 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AppServices] BlackBody connect failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 앱 종료 시 챔버 정지 후 서비스들을 순차 정리한다. <c>_disposed</c> 가드로
        /// 재진입해도 한 번만 수행한다. 실제 시퀀스는 <see cref="RunShutdownAsync"/>에 있다.
        /// </summary>
        public static async Task DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // 실행 중이던 챔버 제어를 PLC 종료 전에 먼저 정지 → 앱을 닫아도 히터가 무인 가열되지
            // 않게 한다. 레시피 취소 경로는 StopChamberAsync를 타지 않으므로(정상 완료 시에만 호출)
            // 여기가 유일한 안전망이다.
            Func<Task>? stopChamber = null;
            if (PlcController is { } plc) stopChamber = plc.StopChamberAsync;

            await RunShutdownAsync(
                stopChamber,
                new (string Name, Func<Task> Dispose)[]
                {
                    (nameof(_chamberRecorder),    () => { _chamberRecorder?.Dispose(); return Task.CompletedTask; }),
                    (nameof(_captureRecorder),    () => { _captureRecorder?.Dispose(); return Task.CompletedTask; }),
                    (nameof(PlcStatus),           () => { PlcStatus?.Stop(); return Task.CompletedTask; }),
                    (nameof(ConnectionMonitor),   () => { ConnectionMonitor?.Dispose(); return Task.CompletedTask; }),
                    (nameof(BlackBodyController), () => { BlackBodyController?.Dispose(); return Task.CompletedTask; }),
                    (nameof(NatsService),         () => NatsService is { } nats ? nats.DisposeAsync().AsTask() : Task.CompletedTask),
                    (nameof(PlcController),       () => { (PlcController as IDisposable)?.Dispose(); return Task.CompletedTask; }),
                    (nameof(Db),                  () => { Db?.Dispose(); return Task.CompletedTask; }),
                },
                TimeSpan.FromSeconds(2));
        }

        // 최선-노력 순차 종료: 각 단계를 개별 격리(한 단계 실패가 이후 정리를 막지 않음)한다.
        // 챔버 정지는 죽은 PLC의 쓰기 타임아웃(FEnet 3s×3)이 5초 App.OnExit 예산을 잠식하지 않도록
        // 상한을 둔다. 정적 AppServices에 묶이지 않은 순수 시퀀스라 단위 테스트가 가능하다.
        internal static async Task RunShutdownAsync(
            Func<Task>? stopChamber,
            IReadOnlyList<(string Name, Func<Task> Dispose)> steps,
            TimeSpan stopChamberTimeout)
        {
            if (stopChamber != null)
            {
                try
                {
                    await stopChamber().WaitAsync(stopChamberTimeout);
                }
                catch (Exception ex)
                {
                    string message = string.Format(
                        LocalizationManager.Instance["Plc_ChamberStopFailed"], ex.Message);
                    AlarmSink.Raise(AlarmSeverity.Error, "PLC", message);
                    System.Diagnostics.Debug.WriteLine($"[AppServices] {message}");
                }
            }

            foreach (var (name, dispose) in steps)
            {
                try
                {
                    await dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppServices] {name} dispose failed: {ex.Message}");
                }
            }
        }

        /// <summary>현재 <see cref="Settings"/>를 %LOCALAPPDATA%의 hardware.json에 덮어쓴다.</summary>
        public static void SaveHardwareSettings()
        {
            File.WriteAllText(_hardwareJsonPath, JsonSerializer.Serialize(Settings, _jsonOpts));
        }

        /// <summary>
        /// hardware.json이 있으면 읽고, 없거나 예외로 읽지 못하면 기본값 파일을 새로 써서 반환한다
        /// (손상된 파일은 기본값으로 덮어써진다).
        /// </summary>
        private static HardwareSettings LoadOrCreateSettings(string dir)
        {
            string path = Path.Combine(dir, "hardware.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    HardwareSettings settings = JsonSerializer.Deserialize<HardwareSettings>(json, _jsonOpts) ?? new HardwareSettings();
                    if (CorrectLegacyHardwareSettings(settings))
                        File.WriteAllText(path, JsonSerializer.Serialize(settings, _jsonOpts));
                    return settings;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppServices] hardware.json load failed: {ex.Message}");
                }
            }

            var defaults = new HardwareSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, _jsonOpts));
            System.Diagnostics.Debug.WriteLine($"[AppServices] Created default hardware.json at {path}");
            return defaults;
        }

        internal static bool CorrectLegacyHardwareSettings(HardwareSettings settings)
        {
            bool changed = false;
            if (settings.Plc.BitEmergencyStop == "M2000")
            {
                settings.Plc.BitEmergencyStop = "M901";
                changed = true;
            }
            if (settings.Plc.BitHumidityControl == "D281.0")
            {
                settings.Plc.BitHumidityControl = "D281";
                changed = true;
            }
            foreach (BlackBodyUnitSettings unit in settings.BlackBody.Units)
            {
                if (unit.ConnectionType == BlackBodyConnectionType.Ip && unit.Port == 5000)
                {
                    unit.Port = 5200;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
