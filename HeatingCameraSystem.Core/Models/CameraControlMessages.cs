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
