using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Moq;

namespace HeatingCameraSystem.Tests;

public class UsbPhysicalIdentityTests
{
    private const string CameraA = @"USB\VID_0483&PID_5740&MI_00\6&AAAA&0&0000";
    private const string SerialB = @"USB\VID_0483&PID_5740&MI_01\6&BBBB&0&0001";
    private const string PhysicalContainer = "{BEE28B49-3064-4F6C-969A-2F91700D3AD4}";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    [InlineData(@"USB\VID_0483&PID_5740")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000000000000000000000000000")]
    public void MissingOrInvalidContainer_IsNotReplacedWithVidPid(string? registryValue)
    {
        string container = UsbTopology.ResolvePhysicalContainerId(CameraA, _ => registryValue);

        Assert.Empty(container);
    }

    [Theory]
    [InlineData("{bee28b49-3064-4f6c-969a-2f91700d3ad4}")]
    [InlineData("BEE28B49-3064-4F6C-969A-2F91700D3AD4")]
    [InlineData("bee28b4930644f6c969a2f91700d3ad4")]
    public void RealContainer_IsNormalizedAcrossRegistryRepresentations(string registryValue)
    {
        Assert.Equal(PhysicalContainer, UsbTopology.ResolvePhysicalContainerId(CameraA, _ => registryValue));
    }

    [Fact]
    public void LookupFailure_LeavesIdentityUnresolved()
    {
        string container = UsbTopology.ResolvePhysicalContainerId(CameraA,
            _ => throw new UnauthorizedAccessException("registry unavailable"));

        Assert.Empty(container);
    }

    [Fact]
    public void MissingDeviceInstance_DoesNotReadAnotherRegistryKey()
    {
        int lookups = 0;
        string container = UsbTopology.ResolvePhysicalContainerId(string.Empty, _ =>
        {
            lookups++;
            return PhysicalContainer;
        });

        Assert.Empty(container);
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task PartialDevicesWithoutContainerIds_CannotPairVideoAWithSerialBOrReuseLegacyIndex()
    {
        // The former fallback collapses different physical instances into the same VID/PID.
        Assert.Equal(UsbTopology.NormalizeParent(CameraA), UsbTopology.NormalizeParent(SerialB));
        string videoContainer = UsbTopology.ResolvePhysicalContainerId(CameraA, _ => null);
        string serialContainer = UsbTopology.ResolvePhysicalContainerId(SerialB, _ => null);
        var video = new DiscoveredCamera
        {
            HardwareId = CameraA, FriendlyName = "CLTC_T_VGA A", OpenCvIndex = 0,
            UsbParentId = videoContainer
        };
        var cameraEnumerator = new Mock<ICameraEnumerator>();
        cameraEnumerator.Setup(cameras => cameras.Enumerate()).Returns(new[] { video });
        var serialEnumerator = new Mock<IUsbSerialEnumerator>();
        serialEnumerator.Setup(ports => ports.Enumerate()).Returns(new[]
        {
            new DiscoveredSerialPort("COM8", "Camera B serial", SerialB, serialContainer)
        });
        int serialOpens = 0;
        var pairing = new CameraComPairingService(cameraEnumerator.Object, serialEnumerator.Object, port =>
        {
            serialOpens++;
            return new FakeCameraSerialClient(port);
        }, new HardwareSettings());

        IReadOnlyList<CameraComPair> pairs = await pairing.GetPairsAsync();

        CameraComPair result = Assert.Single(pairs);
        Assert.Equal(PairingStatus.Unpaired, result.Status);
        Assert.Null(result.SerialPort);
        Assert.Null(result.CameraSerialNumber);
        Assert.Equal(0, serialOpens);
        var configured = new List<CameraDescriptor>
        {
            new("saved-B", 0, "B", "COM8", CameraSerialNumber: "000100002",
                UsbContainerId: UsbTopology.NormalizeParent(SerialB))
        };
        var devices = new[] { new VideoDevice(0, CameraA, videoContainer, "CLTC_T_VGA A") };
        Assert.Equal(0, CameraAutoRegistrar.Register(configured, pairs, "PC"));
        Assert.Equal(0, CameraAutoRegistrar.RegisterVideoOnly(configured, devices, "PC"));
        Assert.Equal(1, VideoIndexReconciler.Reconcile(configured, devices));
        CameraDescriptor offline = Assert.Single(configured);
        Assert.Equal("saved-B", offline.AgentId);
        Assert.Equal("000100002", offline.CameraSerialNumber);
        Assert.Equal(-1, offline.OpenCvIndex);
    }
}
