namespace HeatingCameraSystem.Core.Models
{
    // One DirectShow video-input device as OpenCV sees it. Index is the exact integer that
    // VideoCapture(index, DSHOW) opens (devices enumerated in OpenCV's DirectShow order); ContainerId
    // is the Windows ContainerID of the physical device, matched against CameraDescriptor.UsbContainerId
    // to rebind the index after a replug shuffles the enumeration order.
    public sealed record VideoDevice(int Index, string DevicePath, string ContainerId, string FriendlyName);
}
