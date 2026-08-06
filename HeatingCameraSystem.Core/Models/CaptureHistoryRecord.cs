using System;

namespace HeatingCameraSystem.Core.Models
{
    public class CaptureHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string CameraId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string CameraAlias { get; set; } = string.Empty;
        public int? CameraIndex { get; set; }
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public string RecipeStepId { get; set; } = string.Empty;
    }
}
