namespace HeatingCameraSystem.Core.Models
{
    public sealed class CameraPairingEntry
    {
        public string CameraUsbParentId { get; set; } = "";
        public string PortName { get; set; } = "";
        public string? ClSerialNumber { get; set; }
        public string? DisplayName { get; set; }
    }
}
