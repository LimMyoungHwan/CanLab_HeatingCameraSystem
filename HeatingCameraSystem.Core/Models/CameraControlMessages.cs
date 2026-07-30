using System;

namespace HeatingCameraSystem.Core.Models
{
    public class CameraControlMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public string Op { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public static class CameraControlOps
    {
        public const string Run = "run";
        public const string Stop = "stop";
        public const string ShutterOpen = "shutterOpen";
        public const string ShutterClose = "shutterClose";
        public const string Capture = "capture";
        public const string Nuc = "nuc";
        public const string SaveConfig = "saveConfig";
        public const string RefreshInfo = "refreshInfo";

        // [S7] Per-camera runtime load/unload — distinct from serial Run/Stop above.
        // The Manager (redefined AgentSupervisor) uses these to load/unload a single camera
        // runtime inside the one AgentUI process without killing the process. runtimeLoad is
        // idempotent (re)load, so a Restart is a single Load message (no unload→load race).
        public const string RuntimeLoad = "runtimeLoad";
        public const string RuntimeUnload = "runtimeUnload";
    }

    public class CameraControlAckMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public string Op { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
