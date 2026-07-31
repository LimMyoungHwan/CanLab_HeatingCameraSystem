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
        Assert.Contains(AlarmSink.Entries, e => e is not null && e.Severity == AlarmSeverity.Error && e.Source == "PLC");
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

    [Fact]
    public void ErrorEdge_ShowsPopup_Once_AndKeepsAlarm_WhileLatched()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var dialog = new Mock<IDialogService>();
        var vm = new DashboardViewModel(plc.Object, null, null, startTimers: false, dialogService: dialog.Object);

        vm.HandlePlcErrors(ErrorAt(1));
        vm.HandlePlcErrors(ErrorAt(1));

        dialog.Verify(d => d.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Contains(AlarmSink.Entries, e => e.Severity == AlarmSeverity.Error && e.Source == "PLC");
    }

    [Fact]
    public void FaultedStop_SurfacesAlarms_AndDoesNotThrow()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        plc.Setup(p => p.TriggerEmergencyStopAsync()).ThrowsAsync(new InvalidOperationException("estop boom"));
        plc.Setup(p => p.StopChamberAsync()).ThrowsAsync(new InvalidOperationException("chamber boom"));
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));

        Assert.Contains(AlarmSink.Entries, e => e is not null && e.Severity == AlarmSeverity.Error && e.Message.Contains("비상 정지 실패"));
        Assert.Contains(AlarmSink.Entries, e => e is not null && e.Severity == AlarmSeverity.Error && e.Message.Contains("챔버 정지 실패"));
    }

    [Fact]
    public void HangingEmergencyStop_DoesNotBlockChamberStopAttempt()
    {
        var chamberCalled = false;
        var plc = new Mock<IPlcController>();
        plc.Setup(p => p.StopChamberAsync())
            .Callback(() => chamberCalled = true)
            .Returns(Task.CompletedTask);
        plc.Setup(p => p.TriggerEmergencyStopAsync())
            .Returns(new TaskCompletionSource<bool>().Task);

        var vm = CreateVm(plc);
        vm.HandlePlcErrors(ErrorAt(1));

        Assert.True(chamberCalled);
    }

    private static PlcStatusSnapshot Snapshot(bool[]? errorBits = null, float servoX = 0f, float servoY = 0f) =>
        new PlcStatusSnapshot
        {
            ErrorBits = errorBits ?? new bool[PlcDeviceCatalog.ErrorNames.Length],
            ServoXPosition = servoX,
            ServoYPosition = servoY
        };

    [Fact]
    public void ErrorEdge_GatesRecipeStart()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        Assert.True(vm.StartRecipeCommand.CanExecute(null));

        vm.HandlePlcErrors(ErrorAt(1));

        Assert.False(vm.StartRecipeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearError_OffOrigin_RemainsGated()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));
        plc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(Snapshot(servoX: 5f, servoY: 5f));
        await vm.RefreshPlcSnapshotAsync();

        Assert.False(vm.StartRecipeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearError_AtOrigin_Unlocks()
    {
        AlarmSink.Entries.Clear();
        var plc = new Mock<IPlcController>();
        var vm = CreateVm(plc);

        vm.HandlePlcErrors(ErrorAt(1));
        plc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(Snapshot(servoX: 0f, servoY: 0f));
        await vm.RefreshPlcSnapshotAsync();

        Assert.True(vm.StartRecipeCommand.CanExecute(null));
    }

    [Fact]
    public async Task OriginTolerance_Boundary_IsInclusive()
    {
        AlarmSink.Entries.Clear();

        var plcAt = new Mock<IPlcController>();
        var vmAt = CreateVm(plcAt);
        vmAt.HandlePlcErrors(ErrorAt(1));
        plcAt.Setup(p => p.ReadStatusAsync()).ReturnsAsync(Snapshot(servoX: 0.5f, servoY: 0.5f));
        await vmAt.RefreshPlcSnapshotAsync();
        Assert.True(vmAt.StartRecipeCommand.CanExecute(null));

        var plcOver = new Mock<IPlcController>();
        var vmOver = CreateVm(plcOver);
        vmOver.HandlePlcErrors(ErrorAt(1));
        plcOver.Setup(p => p.ReadStatusAsync()).ReturnsAsync(Snapshot(servoX: 0.6f, servoY: 0f));
        await vmOver.RefreshPlcSnapshotAsync();
        Assert.False(vmOver.StartRecipeCommand.CanExecute(null));
    }
}
