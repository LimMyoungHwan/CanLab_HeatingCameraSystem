namespace HeatingCameraSystem.Core.Models
{
    public class CameraMappingConfig
    {
        public string SlotId { get; set; } = string.Empty;   // "P01" ~ "P64"
        public string? CameraId { get; set; }                 // "CAM-01" ~ "CAM-48", null = empty
    }
}
