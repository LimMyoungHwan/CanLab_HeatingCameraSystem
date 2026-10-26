using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// AgentUI 로컬 설정. %LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json에 저장된다.
    /// 오프라인 우선: AgentUI는 이 파일을 직접 읽으며 시작할 때 Master/Manager에 절대 의존하지
    /// 않는다(Manager 승인 상태가 나중에, S6/S7에서 이 필드들을 덮어쓴다). 카메라가 없는 PC에서도
    /// UI가 돌도록 기본값은 SimulationMode다.
    /// </summary>
    public sealed class AgentUiConfig
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private AgentUiConfig? _pendingRestart;

        [JsonIgnore]
        public bool RestartRequired => _pendingRestart is not null;

        public bool SimulationMode { get; set; } = false;

        public List<CameraDescriptor> Cameras { get; set; } = new();

        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";

        public string StoragePath { get; set; } = string.Empty;

        public int HeartbeatSeconds { get; set; } = 5;

        public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Y16Raw;

        public int CaptureBurstCount { get; set; } = 1;

        public string EffectiveStorageDir =>
            string.IsNullOrWhiteSpace(StoragePath)
                ? Path.Combine(ConfigDir, "Captures")
                : StoragePath;

        public static string ConfigDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeatingCameraSystem", "AgentUI");

        /// <summary>agentui.json이 있으면 읽고, 없거나 깨졌으면 기본값을 생성·저장해 반환한다.</summary>
        public static AgentUiConfig LoadOrCreate()
        {
            Directory.CreateDirectory(ConfigDir);
            string path = Path.Combine(ConfigDir, "agentui.json");

            if (File.Exists(path))
            {
                try
                {
                    AgentUiConfig? cfg = JsonSerializer.Deserialize<AgentUiConfig>(File.ReadAllText(path), JsonOpts);
                    if (cfg is not null)
                    {
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AgentUiConfig] load failed: {ex.Message}");
                }
            }

            AgentUiConfig defaults = CreateDefaults();
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOpts));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentUiConfig] create failed: {ex.Message}");
            }

            return defaults;
        }

        /// <summary>진행 중인 서비스 설정과 다음 실행용 저장 설정을 분리한다.</summary>
        public AgentUiConfig EditableSnapshot() => (_pendingRestart ?? this).Copy();

        private AgentUiConfig Copy() => new()
        {
            SimulationMode = SimulationMode,
            NatsUrl = NatsUrl,
            StoragePath = StoragePath,
            HeartbeatSeconds = HeartbeatSeconds,
            CaptureImageFormat = CaptureImageFormat,
            CaptureBurstCount = CaptureBurstCount,
            Cameras = Cameras.ToList()
        };

        private bool SameRuntimeSettings(AgentUiConfig other) =>
            SimulationMode == other.SimulationMode && NatsUrl == other.NatsUrl && StoragePath == other.StoragePath
            && HeartbeatSeconds == other.HeartbeatSeconds && CaptureImageFormat == other.CaptureImageFormat
            && CaptureBurstCount == other.CaptureBurstCount;

        /// <summary>
        /// 디스크 기록이 성공한 뒤에만 활성 설정을 변경한다. 소스/연결/저장 서비스가 기동 시 고정하는
        /// 설정이 바뀌면 카메라 목록까지 저장본에 격리하여 재시작 전 실제 장치와 표시를 혼동하지 않는다.
        /// </summary>
        public bool SaveDraft(AgentUiConfig draft, bool requireRestart = false)
        {
            AgentUiConfig saved = draft.Copy();
            if (string.IsNullOrWhiteSpace(saved.NatsUrl)) throw new ArgumentException("NATS URL이 비어 있습니다.");
            if (saved.HeartbeatSeconds <= 0 || saved.CaptureBurstCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(draft), "하트비트 간격과 촬영 장수는 1 이상이어야 합니다.");
            if (saved.Cameras.Any(camera => string.IsNullOrWhiteSpace(camera.AgentId)))
                throw new ArgumentException("카메라 AgentId가 비어 있습니다.");
            Write(saved);
            bool restart = requireRestart || !SameRuntimeSettings(saved);
            _pendingRestart = restart ? saved : null;
            if (!restart)
            {
                // Connector의 카메라 목록 참조는 수명 동안 동일하다.
                lock (Cameras)
                {
                    Cameras.Clear();
                    Cameras.AddRange(saved.Cameras);
                }
            }
            return restart;
        }

        /// <summary>핫플러그 자동 저장이 운영자가 저장한 재시작 대기 설정을 덮어쓰지 않게 한다.</summary>
        public void Save() => Write(_pendingRestart ?? this);

        private static void Write(AgentUiConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
            string path = Path.Combine(ConfigDir, "agentui.json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(config, JsonOpts));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static AgentUiConfig CreateDefaults() => new()
        {
            SimulationMode = true,
            Cameras =
            {
                new CameraDescriptor("SimCam_0", 0, "Camera 0"),
                new CameraDescriptor("SimCam_1", 1, "Camera 1"),
            }
        };
    }
}
