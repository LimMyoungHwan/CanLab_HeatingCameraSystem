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
        public static NatsCommunicationService? NatsService { get; private set; }
        public static IPlcController? PlcController { get; private set; }
        public static IBlackBodyController? BlackBodyController { get; private set; }
        public static ISerialShutterController? ShutterController { get; private set; }
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

        // 종료 1회 보장 가드 — DisposeAsync 재진입 시 재-종료 방지.
        private static bool _disposed;

        private static string _hardwareJsonPath = string.Empty;

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

            // Recipes now persist as one JSON file each under <dir>/recipe (+ <dir>/recipe bak backups).
            var fileRecipeRepo = new FileRecipeRepository(dir);
            try
            {
                // One-time seed from legacy LiteDB, tracked by a persistent _migrations marker so
                // recipes the operator later deletes do not resurrect on a later empty-folder startup.
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
                ShutterController = new FakeSerialShutterController();
                CameraSerialClientFactory = portName => new FakeCameraSerialClient(portName);
                LiveThermalCamera         = new FakeLiveThermalCamera();
                CameraPairingService      = new FakeCameraComPairingService();
                System.Diagnostics.Debug.WriteLine("[AppServices] SimulationMode=true -> using Fake PLC + Fake Shutter + Fake Camera/Pairing");
            }
            else
            {
                PlcController     = new PlcXgtClient(Settings.Plc);
                ShutterController = new SerialShutterController(Settings.Serial);
                CameraSerialClientFactory = portName => new ClSerialCameraClient(portName);
                LiveThermalCamera         = new CltcLiveThermalCamera();
                var cameraEnumerator      = new WmiCameraEnumerator();
                var usbSerialEnumerator   = new WmiUsbSerialEnumerator();
                CameraPairingService      = new CameraComPairingService(
                    cameraEnumerator, usbSerialEnumerator, CameraSerialClientFactory, Settings);
            }

            BlackBodyController = CreateBlackBodyController(Settings, PlcController);

            RecipeEngine = new RecipeEngine(PlcController, NatsService, HistoryRepo, Settings.RecipeEngine, ImageCacheDir, CameraDeviceRepo, BlackBodyController);
            ConnectionMonitor = new ConnectionMonitorService(PlcController, ShutterController, Settings);
            if (!Settings.SimulationMode) ConnectionMonitor.Start();

            PlcStatus = new PlcStatusService(PlcController, Settings.BlackBody.Enabled ? BlackBodyController : null);
            PlcStatus.Start();

            _chamberRecorder = new ChamberHistoryRecorder(ChamberHistoryRepo, PlcStatus);
        }

        public static IBlackBodyController CreateBlackBodyController(HardwareSettings settings, IPlcController plc)
        {
            if (settings.SimulationMode) return new FakeBlackBodyController();
            return new SrBlackBodyController(settings.BlackBody, plc: plc);
        }

        public static async Task TryConnectServicesAsync()
        {
            try
            {
                await NatsService!.ConnectAsync(Settings.Nats.Url);
                System.Diagnostics.Debug.WriteLine("[AppServices] NATS connected.");
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

            if (Settings.SimulationMode && ShutterController is { IsConnected: false })
            {
                try
                {
                    await ShutterController.ConnectAsync();
                    System.Diagnostics.Debug.WriteLine("[AppServices] Fake shutter connected.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppServices] Fake shutter connect failed: {ex.Message}");
                }
            }
        }

        public static async Task ApplySerialSettingsLocallyAsync(Core.Models.CameraSerialSettings s)
        {
            ShutterController?.Dispose();

            if (Settings.SimulationMode)
            {
                ShutterController = new FakeSerialShutterController();
            }
            else
            {
                ShutterController = new SerialShutterController(new Core.Config.SerialSettings
                {
                    PortName = s.PortName,
                    BaudRate = s.BaudRate,
                    DataBits = s.DataBits,
                    Parity   = s.Parity,
                    StopBits = s.StopBits
                });
            }

            try
            {
                await ShutterController.ConnectAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppServices] Local shutter reconnect failed: {ex.Message}");
            }
        }

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
                    (nameof(PlcStatus),           () => { PlcStatus?.Stop(); return Task.CompletedTask; }),
                    (nameof(ConnectionMonitor),   () => { ConnectionMonitor?.Dispose(); return Task.CompletedTask; }),
                    (nameof(ShutterController),   () => { ShutterController?.Dispose(); return Task.CompletedTask; }),
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

        public static void SaveHardwareSettings()
        {
            File.WriteAllText(_hardwareJsonPath, JsonSerializer.Serialize(Settings, _jsonOpts));
        }

        private static HardwareSettings LoadOrCreateSettings(string dir)
        {
            string path = Path.Combine(dir, "hardware.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<HardwareSettings>(json, _jsonOpts) ?? new HardwareSettings();
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
    }
}
