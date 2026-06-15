using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Agent.Services;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Agent
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string agentId  = args.Length > 0 ? args[0] : Environment.MachineName;
            string natsUrl  = args.Length > 1 ? args[1] : "nats://127.0.0.1:4222";
            string storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageStorage");

            using var cameraService = new CameraCaptureService(storagePath);
            bool cameraReady = cameraService.InitializeCamera(0);
            if (!cameraReady)
                Console.WriteLine($"[{agentId}] Camera unavailable — capture commands will report failure.");

            await using var nats = new NatsCommunicationService();
            await nats.ConnectAsync(natsUrl);
            Console.WriteLine($"[{agentId}] Connected to NATS ({natsUrl})");

            await nats.SubscribeCaptureCommandAsync(agentId, cmd =>
            {
                _ = HandleCaptureAsync(cmd, cameraService, nats, agentId);
            });

            using var heartbeat = new Timer(async _ =>
            {
                await nats.PublishAgentStatusAsync(new AgentStatusMessage
                {
                    AgentId = agentId,
                    CameraIndex = 0,
                    IsCameraReady = cameraReady,
                    Timestamp = DateTime.UtcNow
                });
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            Console.WriteLine($"[{agentId}] Agent running. Press Ctrl+C to stop.");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

            Console.WriteLine($"[{agentId}] Shutting down.");
            cameraService.Stop();
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
