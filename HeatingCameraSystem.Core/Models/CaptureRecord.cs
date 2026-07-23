using System;

namespace HeatingCameraSystem.Core.Models
{
    public sealed class CaptureRecord
    {
        public Guid Id { get; set; }
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public ushort Min { get; set; }
        public ushort Max { get; set; }
        public string? RecipeStepId { get; set; }
        public string Y16Path { get; set; } = string.Empty;
        public string JsonPath { get; set; } = string.Empty;
        public string? PngPath { get; set; }
    }
}
