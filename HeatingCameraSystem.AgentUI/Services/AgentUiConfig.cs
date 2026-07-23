using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// AgentUI-local configuration, stored under
    /// %LOCALAPPDATA%\HeatingCameraSystem\AgentUI\agentui.json. Offline-first: AgentUI reads
    /// this directly and never depends on Master/Manager to start (Manager approval state
    /// overrides these fields later, in S6/S7). Defaults to SimulationMode so the UI runs on a
    /// machine with no cameras attached.
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

        public static string ConfigDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeatingCameraSystem", "AgentUI");

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
