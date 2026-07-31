using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using Moq;

namespace HeatingCameraSystem.Tests;

public class DashboardPlcErrorStopTests
{
    private static bool[] ErrorAt(int index)
    {
        var bits = new bool[PlcDeviceCatalog.ErrorNames.Length];
        bits[index] = true;
        return bits;
    }

    private static DashboardViewModel CreateVm(Mock<IPlcController> plc) =>
        new DashboardViewModel(plc.Object, null, null, startTimers: false);

    [Fact]
    public void ErrorEdge_StopsAll_AndRaisesErrorAlarm()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));

        plc.Verify(p => p.TriggerEmergencyStopAsync(), Times.Once);
        plc.Verify(p => p.StopChamberAsync(), Times.Once);
        Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error && e.Source == "PLC");
        Assert.True(vm.IsEmergencyStop);
    }

    [Fact]
    public void HeldError_DoesNotRefire_WhileLatched()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));
        vm.HandlePlcErrors(ErrorAt(1));

        plc.Verify(p => p.TriggerEmergencyStopAsync(), Times.Once);
        plc.Verify(p => p.StopChamberAsync(), Times.Once);
    }

    [Fact]
    public void ClearThenError_ReArms_FiresAgain()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));
        vm.HandlePlcErrors(new bool[PlcDeviceCatalog.ErrorNames.Length]);
        vm.HandlePlcErrors(ErrorAt(1));

        plc.Verify(p => p.TriggerEmergencyStopAsync(), Times.Exactly(2));
        plc.Verify(p => p.StopChamberAsync(), Times.Exactly(2));
        Assert.True(vm.IsEmergencyStop);
    }
}
