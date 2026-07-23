using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Self-describing sidecar for one saved thermal capture. Persisted next to the raw
    /// <c>.y16</c> file so an archive stays interpretable without the local index/database.
    /// The raw payload is little-endian <see cref="ushort"/> masked to 14 bits.
    /// </summary>
    public sealed class CaptureMetadata
    {
        public string AgentId { get; set; } = string.Empty;
        public int CameraIndex { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string PixelFormat { get; set; } = "Y16_14bit_LE";
        public DateTimeOffset TimestampUtc { get; set; }
        public ushort Min { get; set; }
        public ushort Max { get; set; }
        public string? RecipeStepId { get; set; }
        public string Y16File { get; set; } = string.Empty;
    }
}
