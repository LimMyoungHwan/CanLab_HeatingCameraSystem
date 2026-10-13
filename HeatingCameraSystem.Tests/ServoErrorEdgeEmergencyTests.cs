using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;

namespace HeatingCameraSystem.Tests;

public class ServoErrorEdgeEmergencyTests
{
    private static bool[] ErrorAt(params int[] indices)
    {
        var bits = new bool[PlcDeviceCatalog.ErrorNames.Length];
        foreach (int i in indices) bits[i] = true;
        return bits;
    }

    private static PlcStatusService ServiceWith(bool[] bits)
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ErrorBits = bits });
        return new PlcStatusService(plc.Object);
    }

    [Fact]
    public async Task ServoXYEdges_DoNotRaiseEmergency()
    {
        var service = ServiceWith(ErrorAt(1, 2));
        bool raised = false;
        service.ErrorRaised += (_, _) => raised = true;

        await service.RefreshAsync();

        Assert.False(raised);
    }

    [Fact]
    public async Task OtherBitEdge_RaisesEmergency()
    {
        var service = ServiceWith(ErrorAt(3));
        bool raised = false;
        service.ErrorRaised += (_, _) => raised = true;

        await service.RefreshAsync();

        Assert.True(raised);
    }

    [Fact]
    public async Task ServoXY_WithOtherBit_RaisesEmergency()
    {
        var service = ServiceWith(ErrorAt(1, 2, 3));
        bool raised = false;
        service.ErrorRaised += (_, _) => raised = true;

        await service.RefreshAsync();

        Assert.True(raised);
    }
}
