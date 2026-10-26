using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Tests;

public class CameraInventoryDeltaTests
{
    private static DiscoveredCamera Camera(string id, int index = 0)
        => new() { HardwareId = "hw-" + id, UsbParentId = id, OpenCvIndex = index, FriendlyName = "CLTC_T_VGA" };
    private static DiscoveredSerialPort Port(string id, string port)
        => new(port, port, "hw-" + port, id);

    [Fact]
    public void UnrelatedPnpOrComChanges_DoNotResetConnectedCameras()
    {
        var cameras = new[] { Camera("A") };
        var oldPorts = new[] { Port("A", "COM7") };
        var newPorts = new[] { Port("A", "COM7"), Port("USB-adapter", "COM8") };

        Assert.Empty(CameraInventoryDelta.Calculate(cameras, cameras, oldPorts, newPorts));
    }

    [Fact]
    public void RemovingLastCamera_EmitsRemovalWithItsPreviousIdentity()
    {
        var camera = Camera("A");
        PnpChange change = Assert.Single(CameraInventoryDelta.Calculate(new[] { camera },
            Array.Empty<DiscoveredCamera>(), new[] { Port("A", "COM7") }, Array.Empty<DiscoveredSerialPort>()));

        Assert.Equal(PnpChangeType.Removal, change.ChangeType);
        Assert.Same(camera, change.Camera);
    }

    [Fact]
    public void RemovingOneCamera_DoesNotReportTheOtherAsChanged()
    {
        var a = Camera("A");
        var b = Camera("B", 1);
        PnpChange change = Assert.Single(CameraInventoryDelta.Calculate(new[] { a, b }, new[] { b },
            new[] { Port("A", "COM7"), Port("B", "COM8") }, new[] { Port("B", "COM8") }));

        Assert.Equal(PnpChangeType.Removal, change.ChangeType);
        Assert.Same(a, change.Camera);
    }

    [Fact]
    public void CameraComPortChange_EmitsOnlyTheAffectedCamera()
    {
        var a = Camera("A");
        var b = Camera("B", 1);
        var cameras = new[] { a, b };
        PnpChange change = Assert.Single(CameraInventoryDelta.Calculate(cameras, cameras,
            new[] { Port("A", "COM7"), Port("B", "COM8") },
            new[] { Port("A", "COM9"), Port("B", "COM8") }));

        Assert.Equal(PnpChangeType.Arrival, change.ChangeType);
        Assert.Same(a, change.Camera);
    }
}
