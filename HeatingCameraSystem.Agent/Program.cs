using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Agent.Services;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Agent
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var config = LoadOrCreateConfig(args);
            string storagePath = Path.IsPathRooted(config.StoragePath)
                ? config.StoragePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, config.StoragePath);

            using var cameraService = new CameraCaptureService(storagePath);
            bool cameraReady = cameraService.InitializeCamera(config.CameraIndex);
            if (!cameraReady)
                Console.WriteLine($"[{config.AgentId}] Camera {config.CameraIndex} unavailable — capture commands will report failure.");

            await using var nats = new NatsCommunicationService();
            await nats.ConnectAsync(config.NatsUrl);
            Console.WriteLine($"[{config.AgentId}] Connected to NATS ({config.NatsUrl})");

            await nats.SubscribeCaptureCommandAsync(config.AgentId, cmd =>
            {
                _ = HandleCaptureAsync(cmd, cameraService, nats, config.AgentId);
            });

            SerialShutterController? shutterController = null;
            await nats.SubscribeSerialConfigAsync(config.AgentId, msg =>
            {
                _ = ApplySerialConfigAsync(msg);
            });

            using var heartbeat = new Timer(async _ =>
            {
                await nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = config.AgentId,
                    CameraIndex = config.CameraIndex,
                    IsCameraReady = cameraReady,
                    Timestamp = DateTime.UtcNow
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
                    shutterController = new SerialShutterController(new SerialSettings
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
                try
                {
                    config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), opts) ?? new AgentConfig();
                }
                catch
                {
                    config = new AgentConfig();
                }
            }
            else
            {
                config = new AgentConfig { AgentId = Environment.MachineName };
                File.WriteAllText(path, JsonSerializer.Serialize(config, opts));
                Console.WriteLine($"Created default agent.json at {path}");
            }

            if (string.IsNullOrWhiteSpace(config.AgentId))
                config.AgentId = Environment.MachineName;

            if (args.Length > 0) config.AgentId = args[0];
            if (args.Length > 1) config.NatsUrl = args[1];

            return config;
        }

        private static async Task HandleCaptureAsync(
            CaptureCommandMessage cmd,
            CameraCaptureService camera,
            NatsCommunicationService nats,
            string agentId)
        {
            bool success = camera.CaptureFrame(out string savedPath);
            await nats.PublishCaptureResultAsync(new CaptureResultMessage
            {
                AgentId      = agentId,
                RecipeStepId = cmd.RecipeStepId,
                IsSuccess    = success,
                ImagePath    = savedPath,
                Timestamp    = DateTime.UtcNow
            });
            Console.WriteLine($"[{agentId}] Step {cmd.RecipeStepId}: {(success ? "OK" : "FAIL")} -> {savedPath}");
        }
    }
}
