using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Moq;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// PLC 폴링과 흑체 폴링의 상호 독립성(대칭) 회귀 테스트. 한쪽이 죽어도 다른 쪽은 계속 갱신되어야 한다.
/// </summary>
public class PlcBlackBodyIsolationTests
{
    private static Mock<IPlcController> HealthyPlc(float temperature = 42f)
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { CurrentTemperature = temperature });
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);
        return plc;
    }

    private static Mock<IBlackBodyController> BlackBody(int count = 2)
    {
        var bb = new Mock<IBlackBodyController>();
        bb.SetupGet(x => x.Count).Returns(count);
        return bb;
    }

    [Fact]
    public async Task DeadBlackBodyUnit_DoesNotAffectPlcStatus()
    {
        var plc = HealthyPlc();
        var bb = BlackBody();
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ThrowsAsync(new TimeoutException("unit 0 hung"));
        bb.Setup(x => x.GetCurrentTemperatureAsync(1)).ReturnsAsync(40.2f);
        bb.Setup(x => x.GetTargetTemperatureAsync(1)).ReturnsAsync(45f);
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();

        Assert.True(service.IsConnected);
        Assert.Equal(42f, service.Snapshot.CurrentTemperature);
    }

    [Fact]
    public async Task DeadBlackBodyUnit_DoesNotAffectHealthyUnit()
    {
        var plc = HealthyPlc();
        var bb = BlackBody();
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ThrowsAsync(new TimeoutException("unit 0 hung"));
        bb.Setup(x => x.GetCurrentTemperatureAsync(1)).ReturnsAsync(40.2f);
        bb.Setup(x => x.GetTargetTemperatureAsync(1)).ReturnsAsync(45f);
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();

        Assert.True(service.BlackBody1Faulted);
        Assert.False(service.BlackBody2Faulted);
        Assert.Equal(40.2f, service.BlackBody2Pv);
        Assert.Equal(45f, service.BlackBody2Sv);
        plc.Verify(x => x.WriteBlackBodyTemperaturesAsync(1, 40.2f, 45f), Times.Once);
        plc.Verify(x => x.WriteBlackBodyTemperaturesAsync(0, It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public async Task DeadPlc_DoesNotStopBlackBodyReads()
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ThrowsAsync(new InvalidOperationException("plc down"));
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);
        var bb = BlackBody();
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ReturnsAsync(30.1f);
        bb.Setup(x => x.GetTargetTemperatureAsync(0)).ReturnsAsync(35f);
        bb.Setup(x => x.GetCurrentTemperatureAsync(1)).ReturnsAsync(40.2f);
        bb.Setup(x => x.GetTargetTemperatureAsync(1)).ReturnsAsync(45f);
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();

        Assert.False(service.IsConnected);
        Assert.True(service.IsBlackBodyConnected);
        Assert.Equal(30.1f, service.BlackBody1Pv);
        Assert.Equal(40.2f, service.BlackBody2Pv);
    }

    [Fact]
    public async Task BlackBodyFailure_DoesNotClearPlcConnectionState()
    {
        var plc = HealthyPlc();
        var bb = BlackBody();
        bb.Setup(x => x.GetCurrentTemperatureAsync(It.IsAny<int>())).ThrowsAsync(new TimeoutException("all units hung"));
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();

        Assert.True(service.IsConnected);
        Assert.False(service.IsBlackBodyConnected);
        Assert.True(service.BlackBody1Faulted);
        Assert.True(service.BlackBody2Faulted);
    }

    [Fact]
    public async Task PlcMirrorFailure_DoesNotFaultTheBlackBodyUnit()
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot());
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .ThrowsAsync(new InvalidOperationException("mirror write refused"));
        var bb = BlackBody(1);
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ReturnsAsync(30.1f);
        bb.Setup(x => x.GetTargetTemperatureAsync(0)).ReturnsAsync(35f);
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();

        Assert.False(service.BlackBody1Faulted);
        Assert.True(service.IsBlackBodyConnected);
        Assert.Equal(30.1f, service.BlackBody1Pv);
    }

    [Fact]
    public async Task DirectBlackBodyReadings_OverridePlcRegisterValues()
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ReturnsAsync(() => new PlcStatusSnapshot { BlackBody1Pv = 11f, BlackBody1Sv = 12f });
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);
        var bb = BlackBody(1);
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ReturnsAsync(30.1f);
        bb.Setup(x => x.GetTargetTemperatureAsync(0)).ReturnsAsync(35f);
        var service = new PlcStatusService(plc.Object, bb.Object);

        await service.RefreshAsync();
        await service.RefreshAsync();

        Assert.Equal(30.1f, service.Snapshot.BlackBody1Pv);
        Assert.Equal(35f, service.Snapshot.BlackBody1Sv);
    }

    [Fact]
    public async Task BlackBodyUpdated_FiresEvenWhenPlcIsDown()
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ThrowsAsync(new InvalidOperationException("plc down"));
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);
        var bb = BlackBody(1);
        bb.Setup(x => x.GetCurrentTemperatureAsync(0)).ReturnsAsync(30.1f);
        bb.Setup(x => x.GetTargetTemperatureAsync(0)).ReturnsAsync(35f);
        var service = new PlcStatusService(plc.Object, bb.Object);
        int fired = 0;
        service.BlackBodyUpdated += (_, _) => fired++;

        await service.RefreshAsync();

        Assert.Equal(1, fired);
    }
}
