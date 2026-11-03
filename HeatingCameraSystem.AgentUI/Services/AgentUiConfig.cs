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

        /// <summary>다수 카메라 USB 부하 조정값. 설정 화면 또는 agentui.json에서 편집하며 재시작 후 적용된다.</summary>
        public AgentUiTuning Tuning { get; set; } = new();

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
            Tuning = (Tuning ?? new AgentUiTuning()) with { },
            Cameras = Cameras.ToList()
        };

        private bool SameRuntimeSettings(AgentUiConfig other) =>
            SimulationMode == other.SimulationMode && NatsUrl == other.NatsUrl && StoragePath == other.StoragePath
            && HeartbeatSeconds == other.HeartbeatSeconds && CaptureImageFormat == other.CaptureImageFormat
            && CaptureBurstCount == other.CaptureBurstCount && EffectiveTuning == other.EffectiveTuning;

        /// <summary>
        /// 디스크 기록이 성공한 뒤에만 활성 설정을 변경한다. 소스/연결/저장 서비스가 기동 시 고정하는
        /// 설정이 바뀌면 카메라 목록까지 저장본에 격리하여 재시작 전 실제 장치와 표시를 혼동하지 않는다.
        /// </summary>
        public bool SaveDraft(AgentUiConfig draft, bool requireRestart = false)
        {
            if (draft.Tuning is null || draft.Tuning != draft.Tuning.Normalized())
                throw new ArgumentOutOfRangeException(nameof(draft), AgentUiTuning.RangeText);
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

        /// <summary>LoadOrCreate 결과의 Tuning이 null이거나 범위 밖이면 기본값으로 되돌린다.</summary>
        public AgentUiTuning EffectiveTuning => (Tuning ?? new AgentUiTuning()).Normalized();

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

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// PCIe USB 카드·연장 케이블 환경에서 동시 USB 트래픽을 줄이기 위한 현장 조정값(밀리초).
    /// 기본값은 기존 동작과 같고, 0 이하·범위 밖 값은 기본값으로 대체한다.
    /// </summary>
    public sealed record AgentUiTuning
    {
        /// <summary>카메라별 S/N·FPA·LSB 시리얼 표시 갱신 주기. 기본 1000.</summary>
        public int SerialPollIntervalMs { get; set; } = 1000;

        /// <summary>카메라 라이브 시작 사이 간격. 0이면 동시에 시작한다. 기본 0.</summary>
        public int CameraStartStaggerMs { get; set; } = 0;

        /// <summary>USB 장치 추가·제거 이벤트를 모아 재열거하기까지의 대기. 기본 1000.</summary>
        public int HotplugDebounceMs { get; set; } = 1000;

        /// <summary>카메라별 COM 열기+S/N 확인 제한 시간. 넘으면 해당 카메라만 시리얼 비활성. 기본 5000.</summary>
        public int SerialOpenTimeoutMs { get; set; } = 5000;

        /// <summary>전체 카메라 COM 쌍 탐색(포트마다 S/N 읽기)의 제한 시간. 넘으면 저장된 구성으로 시작. 기본 30000.</summary>
        public int PairingTimeoutMs { get; set; } = 30_000;

        public const string RangeText =
            "USB 부하 조정값 범위: 시리얼 폴링 200~600000ms, 시작 간격 0~60000ms, 디바운스 200~60000ms, "
            + "COM 열기 제한 500~120000ms, 쌍 탐색 제한 1000~600000ms.";

        public AgentUiTuning Normalized() => new()
        {
            SerialPollIntervalMs = SerialPollIntervalMs is >= 200 and <= 600_000 ? SerialPollIntervalMs : 1000,
            CameraStartStaggerMs = CameraStartStaggerMs is >= 0 and <= 60_000 ? CameraStartStaggerMs : 0,
            HotplugDebounceMs = HotplugDebounceMs is >= 200 and <= 60_000 ? HotplugDebounceMs : 1000,
            SerialOpenTimeoutMs = SerialOpenTimeoutMs is >= 500 and <= 120_000 ? SerialOpenTimeoutMs : 5000,
            PairingTimeoutMs = PairingTimeoutMs is >= 1000 and <= 600_000 ? PairingTimeoutMs : 30_000,
        };
    }
}
