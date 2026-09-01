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
    /// <summary>
    /// 헤드리스 카메라 Agent 콘솔 앱. exe 옆의 agent.json을 읽거나 CLI 인수로 오버라이드하며,
    /// 캡처 명령 수신 → 캡처 → 결과 발행, 시리얼 설정 적용, 하트비트 발행을 담당한다.
    /// 현재 아키텍처에서는 AgentUI가 주 카메라 호스트이고 이 콘솔 Agent는 보조/진단용이다.
    /// </summary>
    class Program
    {
        /// <summary>
        /// 설정 로드 → Serilog NDJSON 파일 싱크 구성 → 카메라 초기화 → NATS 연결 후
        /// <c>master.cmd.capture.{AgentId}</c>와 <c>master.cmd.capture.all</c>(캡처 명령),
        /// <c>master.config.serial.{AgentId}</c>(시리얼 설정)를 구독하고,
        /// <c>agent.status.{AgentId}</c>로 하트비트를 주기 발행하며 Ctrl+C까지 대기한다.
        /// </summary>
        static async Task Main(string[] args)
        {
            var config = LoadOrCreateConfig(args);

            // Serilog NDJSON 파일 싱크 (Manager LogTailService가 tail)
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

            // [camera-model-select] Design Ref: §2.3 — CameraModel 지정 시 CameraModels\{모델}.json 로드 후 해상도 전달
            string modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraModels");
            var modelSpec = CameraModelSpec.Load(modelsDir, config.CameraModel);
            if (!string.IsNullOrWhiteSpace(config.CameraModel) && modelSpec == null)
                Console.WriteLine($"[{config.AgentId}] CameraModel '{config.CameraModel}' spec not found/invalid in {modelsDir} — using camera default resolution.");

            ICameraCaptureService cameraService = config.SimulationMode
                ? new FakeCameraCaptureService(storagePath, config.AgentId)
                : new CameraCaptureService(storagePath, modelSpec?.Width, modelSpec?.Height);
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

            // 시리얼 설정을 받아 기존 셔터 컨트롤러를 버리고 새로 연결한 뒤,
            // 적용 결과를 agent.config.serial.ack.{AgentId}로 발행한다.
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

        /// <summary>
        /// exe 옆의 agent.json을 읽고, 없으면 기본값을 만든다(CLI 인수가 있으면 파일을 쓰지 않아
        /// 다중 인스턴스 실행이 안전하다). CLI 인수 순서는 AgentId, NatsUrl, CameraIndex,
        /// StoragePath, SimulationMode, LogPath이며 파일 값보다 우선한다.
        /// </summary>
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

        /// <summary>
        /// 공유 잠금으로 agent.json을 읽는다. IOException은 최대 3회 재시도하고,
        /// JSON이 손상되었으면 null을 반환한다(호출부가 기본값으로 대체).
        /// </summary>
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

        /// <summary>
        /// 캡처 명령 한 건을 처리한다. 캡처 동안 상태를 Streaming으로 올렸다가 되돌리고,
        /// 성공 여부와 무관하게 결과를 <c>agent.result.capture.{AgentId}</c>로 발행한다
        /// (저장 파일을 읽을 수 있으면 JPEG 바이트를 함께 실어 보낸다).
        /// </summary>
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
                int shots = cmd.ShotCount > 0 ? cmd.ShotCount : 1;
                for (int i = 0; i < shots; i++)
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
                    Console.WriteLine($"[{agentId}] Step {cmd.RecipeStepId} ({i + 1}/{shots}): {(success ? "OK" : "FAIL")} -> {savedPath} ({(bytes?.Length ?? 0)} bytes)");
                }
            }
            finally
            {
                if (prev != CameraStatus.Offline) status.Current = prev;
            }
        }

        /// <summary>
        /// 하트비트 타이머와 캡처 처리 태스크가 공유하는 현재 카메라 상태 홀더.
        /// Volatile 읽기/쓰기로 스레드 간 가시성을 보장한다.
        /// </summary>
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
