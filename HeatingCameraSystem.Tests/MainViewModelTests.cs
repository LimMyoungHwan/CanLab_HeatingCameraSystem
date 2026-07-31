using System;
using System.Reflection;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class MainViewModelTests
{
    [Fact]
    public void PlcOriginCommand_WhenPlcMissing_SetsAlarmMessage()
    {
        // Arrange
        SetPlcController(null);
        var vm = new MainViewModel();

        // Act
        vm.PlcOriginCommand.Execute(null);

        // Assert
        Assert.Equal("PLC 미초기화", vm.AlarmActionMessage);
    }

    [Fact]
    public async Task PlcOriginCommand_WhenPlcAvailable_SetsPoint1AndMoves()
    {
        // Arrange
        var plc = new Mock<IPlcController>();
        var callSequence = new System.Collections.Generic.List<string>();

        plc.Setup(p => p.SetPointCoordinateAsync(1, 0f, 0f))
            .Callback(() => callSequence.Add("SetPoint"))
            .Returns(Task.CompletedTask);

        plc.Setup(p => p.MoveServoToPositionAsync(1))
            .Callback(() => callSequence.Add("MoveServo"))
            .Returns(Task.CompletedTask);

        SetPlcController(plc.Object);
        var vm = new MainViewModel();

        // Act
        await vm.PlcOriginCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, callSequence.Count);
        Assert.Equal("SetPoint", callSequence[0]);
        Assert.Equal("MoveServo", callSequence[1]);
        Assert.Contains("PLC 원점 복귀 완료", vm.AlarmActionMessage);
        
        plc.Verify(p => p.SetPointCoordinateAsync(1, 0f, 0f), Times.Once);
        plc.Verify(p => p.MoveServoToPositionAsync(1), Times.Once);
    }

    [Fact]
    public async Task PlcOriginCommand_WhenPlcFails_SetsErrorMessage()
    {
        // Arrange
        var plc = new Mock<IPlcController>();
        plc.Setup(p => p.SetPointCoordinateAsync(1, 0f, 0f))
            .ThrowsAsync(new InvalidOperationException("Test Failure"));
            
        SetPlcController(plc.Object);
        var vm = new MainViewModel();

        // Act
        await vm.PlcOriginCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("PLC 원점 복귀 실패: Test Failure", vm.AlarmActionMessage);
    }

    private static void SetPlcController(IPlcController? plc)
    {
        var prop = typeof(AppServices).GetProperty("PlcController", BindingFlags.Public | BindingFlags.Static);
        prop?.DeclaringType?.GetProperty("PlcController")?.SetValue(null, plc, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public, null, null, null);
    }
}
