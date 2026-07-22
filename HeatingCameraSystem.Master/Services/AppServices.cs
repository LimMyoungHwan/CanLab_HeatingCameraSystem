using System;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
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
        public static ICameraMappingRepository MappingRepo { get; private set; } = null!;
        public static ICaptureHistoryRepository HistoryRepo { get; private set; } = null!;
        public static ICameraSerialSettingsRepository CameraSerialSettingsRepo { get; private set; } = null!;
        public static ICameraDeviceRepository CameraDeviceRepo { get; private set; } = null!;
        public static NatsCommunicationService? NatsService { get; private set; }
        public static IPlcController? PlcController { get; private set; }
        public static ISerialShutterController? ShutterController { get; private set; }
        public static RecipeEngine? RecipeEngine { get; private set; }
        public static ConnectionMonitorService? ConnectionMonitor { get; private set; }
        public static ILiveThermalCamera? LiveThermalCamera { get; private set; }
        public static ICameraComPairingService? CameraPairingService { get; private set; }
        public static Func<string, ICameraSerialClient>? CameraSerialClientFactory { get; private set; }

        private static string _hardwareJsonPath = string.Empty;

        public static void Initialize()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeatingCameraSystem");
            Directory.CreateDirectory(dir);

            Settings = LoadOrCreateSettings(dir);
            _hardwareJsonPath = Path.Combine(dir, "hardware.json");
            ImageCacheDir = Path.Combine(dir, "ImageCache");
            Directory.CreateDirectory(ImageCacheDir);

            Db = new LiteDatabase(Path.Combine(dir, "data.db"));
            RecipeRepo = new LiteDbRecipeRepository(Db);
            MappingRepo = new LiteDbCameraMappingRepository(Db);
            HistoryRepo = new LiteDbCaptureHistoryRepository(Db);
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

            RecipeEngine = new RecipeEngine(PlcController, NatsService, HistoryRepo, Settings.RecipeEngine, ImageCacheDir, CameraDeviceRepo);
            ConnectionMonitor = new ConnectionMonitorService(PlcController, ShutterController, Settings);
            if (!Settings.SimulationMode) ConnectionMonitor.Start();
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
                System.Diagnostics.Debug.WriteLine($"[AppServices] NATS connect failed: {ex.Message}");
            }

            try
            {
                await PlcController!.ConnectAsync(Settings.Plc.IpAddress, Settings.Plc.Port);
                System.Diagnostics.Debug.WriteLine("[AppServices] PLC connected.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppServices] PLC connect failed: {ex.Message}");
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
            ConnectionMonitor?.Dispose();
            ShutterController?.Dispose();
            if (NatsService != null) await NatsService.DisposeAsync();
            (PlcController as IDisposable)?.Dispose();
            Db?.Dispose();
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
