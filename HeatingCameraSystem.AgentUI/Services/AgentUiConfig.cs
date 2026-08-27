using System;
using System.Collections.Generic;
using System.IO;
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

        public bool SimulationMode { get; set; } = true;

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

        /// <summary>agentui.json에 기록한다. 설정은 시작 시 한 번만 읽으므로 변경은 다음 실행부터 적용된다.</summary>
        public void Save()
        {
            Directory.CreateDirectory(ConfigDir);
            string path = Path.Combine(ConfigDir, "agentui.json");
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
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
