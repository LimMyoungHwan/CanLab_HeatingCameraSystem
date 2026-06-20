using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Agent.Services;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

namespace HeatingCameraSystem.Agent
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var config = LoadOrCreateConfig(args);

            // Serilog NDJSON file sink (Manager LogTailService가 tail)
            string logDir = string.IsNullOrEmpty(config.LogPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
                : config.LogPath;
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("AgentId", config.AgentId)
                .WriteTo.Console()
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(logDir, "agent-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true)
                .CreateLogger();

            using var loggerFactory = LoggerFactory.Create(b => b.AddSerilog(dispose: false));
            var logger = loggerFactory.CreateLogger<Program>();

            string storagePath = Path.IsPathRooted(config.StoragePath)
                ? config.StoragePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.StoragePath);

            ICameraCaptureService cameraService = config.SimulationMode
                ? new FakeCameraCaptureService(storagePath, config.AgentId)
                : new CameraCaptureService(storagePath);
            using var cameraDisposable = cameraService as IDisposable;

            bool cameraReady = cameraService.InitializeCamera(config.CameraIndex);
            var statusBox = new StatusBox(cameraReady ? CameraStatus.Connected : CameraStatus.Offline);
            if (!cameraReady)
                Console.WriteLine($"[{config.AgentId}] Camera {config.CameraIndex} unavailable — capture commands will report failure.");
            else
                Console.WriteLine($"[{config.AgentId}] Camera idx={config.CameraIndex} ready ({(config.SimulationMode ? "SIM" : "REAL")})");

            await using var nats = new NatsCommunicationService();
            await nats.ConnectAsync(config.NatsUrl);
            Console.WriteLine($"[{config.AgentId}] Connected to NATS ({config.NatsUrl})");

            await nats.SubscribeCaptureCommandAsync(config.AgentId, cmd =>
            {
                _ = HandleCaptureAsync(cmd, cameraService, nats, config.AgentId, statusBox);
            });

            ISerialShutterController? shutterController = null;
            await nats.SubscribeSerialConfigAsync(config.AgentId, msg =>
            {
                _ = ApplySerialConfigAsync(msg);
            });

            using var heartbeat = new Timer(async _ =>
            {
                await nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId      = config.AgentId,
                    CameraIndex  = config.CameraIndex,
                    CameraStatus = statusBox.Current,
                    Timestamp    = DateTime.UtcNow
                });
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(config.HeartbeatIntervalSeconds));

            Console.WriteLine($"[{config.AgentId}] Agent running. Press Ctrl+C to stop.");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

            Console.WriteLine($"[{config.AgentId}] Shutting down.");
            shutterController?.Dispose();
            cameraService.Stop();

            async Task ApplySerialConfigAsync(SerialConfigMessage msg)
            {
                bool   success = true;
                string error   = string.Empty;
                try
                {
                    shutterController?.Disconnect();
                    shutterController = config.SimulationMode
                        ? new FakeSerialShutterController()
                        : new SerialShutterController(new SerialSettings
                        {
                            PortName = msg.Settings.PortName,
                            BaudRate = msg.Settings.BaudRate,
                            DataBits = msg.Settings.DataBits,
                            Parity   = msg.Settings.Parity,
                            StopBits = msg.Settings.StopBits
                        });
                    await shutterController.ConnectAsync();
                }
                catch (Exception ex)
                {
                    success = false;
                    error   = ex.Message;
                }

                await nats.PublishSerialConfigAckAsync(new SerialConfigAckMessage
                {
                    AgentId      = config.AgentId,
                    IsSuccess    = success,
                    ErrorMessage = error,
                    Timestamp    = DateTime.UtcNow
                });

                Console.WriteLine($"[{config.AgentId}] Serial config {msg.Settings.PortName}: {(success ? "OK" : "FAIL")}");
            }
        }

        private static AgentConfig LoadOrCreateConfig(string[] args)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent.json");
            var opts = new JsonSerializerOptions { WriteIndented = true };
            AgentConfig config;

            if (File.Exists(path))
            {
                config = TryReadConfig(path, opts) ?? new AgentConfig();
            }
            else if (args.Length == 0)
            {
                config = new AgentConfig { AgentId = Environment.MachineName };
                try
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(config, opts));
                    Console.WriteLine($"Created default agent.json at {path}");
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[Agent] agent.json not written ({ex.Message}); using in-memory defaults");
                }
            }
            else
            {
                config = new AgentConfig();
                Console.WriteLine("[Agent] No agent.json — using CLI args + defaults (multi-instance safe).");
            }

            if (string.IsNullOrWhiteSpace(config.AgentId))
                config.AgentId = Environment.MachineName;

            if (args.Length > 0) config.AgentId = args[0];
            if (args.Length > 1) config.NatsUrl = args[1];
            if (args.Length > 2 && int.TryParse(args[2], out var camIdx)) config.CameraIndex = camIdx;
            if (args.Length > 3) config.StoragePath = args[3];
            if (args.Length > 4 && bool.TryParse(args[4], out var sim))   config.SimulationMode = sim;
            if (args.Length > 5) config.LogPath = args[5];

            return config;
        }

        private static AgentConfig? TryReadConfig(string path, JsonSerializerOptions opts)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    return JsonSerializer.Deserialize<AgentConfig>(reader.ReadToEnd(), opts);
                }
                catch (IOException)
                {
                    Thread.Sleep(150);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
            return null;
        }

        private static async Task HandleCaptureAsync(
            CaptureCommandMessage cmd,
            ICameraCaptureService camera,
            NatsCommunicationService nats,
            string agentId,
            StatusBox status)
        {
            var prev = status.Current;
            if (prev != CameraStatus.Offline) status.Current = CameraStatus.Streaming;
            try
            {
                bool success = camera.CaptureFrame(out string savedPath);
                byte[]? bytes = null;
                if (success && !string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    try { bytes = File.ReadAllBytes(savedPath); }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"[{agentId}] failed reading captured file for NATS payload: {ex.Message}");
                    }
                }

                await nats.PublishCaptureResultAsync(new CaptureResultMessage
                {
                    AgentId      = agentId,
                    RecipeStepId = cmd.RecipeStepId,
                    IsSuccess    = success,
                    ImagePath    = savedPath,
                    ImageBytes   = bytes,
                    Timestamp    = DateTime.UtcNow
                });
                Console.WriteLine($"[{agentId}] Step {cmd.RecipeStepId}: {(success ? "OK" : "FAIL")} -> {savedPath} ({(bytes?.Length ?? 0)} bytes)");
            }
            finally
            {
                if (prev != CameraStatus.Offline) status.Current = prev;
            }
        }

        private sealed class StatusBox
        {
            private int _current;
            public StatusBox(CameraStatus initial) { _current = (int)initial; }
            public CameraStatus Current
            {
                get => (CameraStatus)Volatile.Read(ref _current);
                set => Volatile.Write(ref _current, (int)value);
            }
        }
    }
}
