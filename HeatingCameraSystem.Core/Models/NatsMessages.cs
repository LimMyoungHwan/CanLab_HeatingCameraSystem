using System;

namespace HeatingCameraSystem.Core.Models
{
    public class AgentStatusMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public bool IsCameraReady { get; set; }
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
    }
}
