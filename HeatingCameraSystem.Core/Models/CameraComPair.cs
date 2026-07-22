namespace HeatingCameraSystem.Core.Models
{
    public sealed record CameraComPair(
        DiscoveredCamera Camera,
        DiscoveredSerialPort? SerialPort,
        string? CameraSerialNumber,
        PairingStatus Status,
        bool IsManualOverride);
}
