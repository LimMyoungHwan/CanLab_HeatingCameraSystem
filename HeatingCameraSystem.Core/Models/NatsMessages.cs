using System;

namespace HeatingCameraSystem.Core.Models
{
    public enum CameraStatus
    {
        Offline,
        Connected,
        Streaming
    }

    public class SerialConfigMessage
    {
        public string               AgentId   { get; set; } = string.Empty;
        public CameraSerialSettings Settings  { get; set; } = new();
        public DateTime             Timestamp { get; set; }
    }

    public class SerialConfigAckMessage
    {
        public string   AgentId      { get; set; } = string.Empty;
        public bool     IsSuccess    { get; set; }
        public string   ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp    { get; set; }
    }

    public class AgentStatusMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public CameraStatus CameraStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CaptureCommandMessage
    {
        public string TargetAgentId { get; set; } = string.Empty; // "all" for broadcast
        public string RecipeStepId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class CaptureResultMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public string RecipeStepId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public byte[]? ImageBytes { get; set; }
    }
}
