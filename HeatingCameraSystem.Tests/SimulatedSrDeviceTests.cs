using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Tests;

public class SimulatedSrDeviceTests
{
    private static BlackBodySettings Sim(double ramp = 100000) => new()
    {
        Enabled = true,
        Simulated = true,
        SimulatedRampCelsiusPerSecond = ramp,
        Units = new() { new BlackBodyUnitSettings(), new BlackBodyUnitSettings() }
    };

    [Fact]
    public async Task Simulated_SetTarget_ReadsBackTarget()
    {
        using var bb = new SrBlackBodyController(Sim());
        await bb.ConnectAsync();

        await bb.SetTemperatureAsync(0, 55f);

        Assert.Equal(55f, await bb.GetTargetTemperatureAsync(0), precision: 1);
    }

    [Fact]
    public async Task Simulated_CurrentConvergesToTarget()
    {
        using var bb = new SrBlackBodyController(Sim(ramp: 100000));
        await bb.ConnectAsync();

        await bb.SetTemperatureAsync(0, 55f);
        await Task.Delay(30);

        Assert.Equal(55f, await bb.GetCurrentTemperatureAsync(0), precision: 0);
    }

    [Fact]
    public async Task Simulated_UnitsAreIndependent()
    {
        using var bb = new SrBlackBodyController(Sim());
        await bb.ConnectAsync();

        await bb.SetTemperatureAsync(0, 40f);
        await bb.SetTemperatureAsync(1, 70f);

        Assert.Equal(40f, await bb.GetTargetTemperatureAsync(0), precision: 1);
        Assert.Equal(70f, await bb.GetTargetTemperatureAsync(1), precision: 1);
    }

    [Fact]
    public void SimulatedDevice_SpeaksTheRealProtocol()
    {
        var device = new SimulatedSrDevice(Sim());
        device.Open();

        device.Write(SrProtocol.SetTemperature(40f));
        device.Write(SrProtocol.GetTargetTemperature());

        Assert.Equal(40f, SrProtocol.ParseFloat(device.Read(), SrProtocol.ParamCurrentSetPoint), precision: 1);
    }

    [Fact]
    public void SimulatedDevice_GetCurrentTemperature_ReturnsFloatFrame()
    {
        var device = new SimulatedSrDevice(Sim(ramp: 1_000_000_000));
        device.Open();

        device.Write(SrProtocol.SetTemperature(30f));
        device.Write(SrProtocol.GetTemperature());

        byte[] frame = device.Read();
        Assert.Equal(SrProtocol.Sync, frame[0]);
        Assert.Equal(30f, SrProtocol.ParseFloat(frame, SrProtocol.ParamCurrentTemperature), precision: 1);
    }
}
