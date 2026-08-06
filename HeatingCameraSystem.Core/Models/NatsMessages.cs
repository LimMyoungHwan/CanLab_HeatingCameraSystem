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
        // Stable routing key: Master maps alias -> live AgentId (recipe targets alias, not the volatile slot).
        public string Alias { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public CameraStatus CameraStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum CaptureSource
    {
        Unknown = 0,
        Recipe = 1,
        Manual = 2,
        AgentUi = 3
    }

    public class CaptureCommandMessage
    {
        public string TargetAgentId { get; set; } = string.Empty; // "all" for broadcast
        public string RecipeStepId { get; set; } = string.Empty;
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;
        public DateTime Timestamp { get; set; }
    }

    public class CaptureResultMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public string RecipeStepId { get; set; } = string.Empty;
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;
        public string CaptureId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public byte[]? ImageBytes { get; set; }
    }

    public class LiveFrameMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public byte[]? ImageBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
