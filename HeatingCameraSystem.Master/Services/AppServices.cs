using System;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace HeatingCameraSystem.Master.Services
{
    public static class AppServices
    {
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        public static HardwareSettings Settings { get; private set; } = new();
        public static LiteDatabase Db { get; private set; } = null!;
        public static IRecipeRepository RecipeRepo { get; private set; } = null!;
        public static ICameraMappingRepository MappingRepo { get; private set; } = null!;
        public static ICaptureHistoryRepository HistoryRepo { get; private set; } = null!;
        public static ICameraSerialSettingsRepository CameraSerialSettingsRepo { get; private set; } = null!;
        public static NatsCommunicationService? NatsService { get; private set; }
        public static IPlcController? PlcController { get; private set; }
        public static ISerialShutterController? ShutterController { get; private set; }
        public static RecipeEngine? RecipeEngine { get; private set; }
        public static ConnectionMonitorService? ConnectionMonitor { get; private set; }

        public static void Initialize()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeatingCameraSystem");
            Directory.CreateDirectory(dir);

            Settings = LoadOrCreateSettings(dir);

            Db = new LiteDatabase(Path.Combine(dir, "data.db"));
            RecipeRepo = new LiteDbRecipeRepository(Db);
            MappingRepo = new LiteDbCameraMappingRepository(Db);
            HistoryRepo = new LiteDbCaptureHistoryRepository(Db);
            CameraSerialSettingsRepo = new LiteDbCameraSerialSettingsRepository(Db);

            NatsService = new NatsCommunicationService();

            if (Settings.SimulationMode)
            {
                PlcController     = new FakePlcController();
                ShutterController = new FakeSerialShutterController();
                System.Diagnostics.Debug.WriteLine("[AppServices] SimulationMode=true -> using Fake PLC + Fake Shutter");
            }
            else
            {
                PlcController     = new PlcModbusClient(Settings.Plc);
                ShutterController = new SerialShutterController(Settings.Serial);
            }

            RecipeEngine = new RecipeEngine(PlcController, NatsService, HistoryRepo);
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
