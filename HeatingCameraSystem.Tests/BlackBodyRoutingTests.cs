using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Plc;

namespace HeatingCameraSystem.Tests;

public class BlackBodyRoutingTests
{
    [Fact]
    public void AppServices_SelectsFakeOnlyForSimulationMode()
    {
        using var plc = new FakePlcController();

        using var fake = AppServices.CreateBlackBodyController(new HardwareSettings { SimulationMode = true }, plc);
        using var real = AppServices.CreateBlackBodyController(new HardwareSettings { SimulationMode = false }, plc);

        Assert.IsType<FakeBlackBodyController>(fake);
        Assert.IsType<PlcBlackBodyAdapter>(real);
    }

    [Fact]
    public async Task PlcBlackBodyAdapter_WritesAndReads_BothBlackBodies_ThroughExternalFEnet()
    {
        int port = GetFreeTcpPort();
        SimulatorSettings settings = SimulatorSettings.CreateDefaults() with
        {
            Endpoint = new EndpointSettings("127.0.0.1", port, "nats://127.0.0.1:4222"),
            OutputPath = Path.Combine(Path.GetTempPath(), "hcs_bb_" + Guid.NewGuid().ToString("N"))
        };

        using var simulator = new FEnetPlcSimulator(settings);
        using var plc = new PlcXgtClient();
        simulator.Start();
        await plc.ConnectAsync("127.0.0.1", port);
        using var bb = new PlcBlackBodyAdapter(plc);

        await bb.SetTemperatureAsync(0, 35.0f);
        await bb.SetTemperatureAsync(1, 45.0f);

        await WaitUntilAsync(async () => await bb.GetCurrentTemperatureAsync(0) >= 34.5f, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(async () => await bb.GetCurrentTemperatureAsync(1) >= 44.5f, TimeSpan.FromSeconds(2));

        Assert.Equal(35.0f, await bb.GetTargetTemperatureAsync(0), precision: 1);
        Assert.Equal(45.0f, await bb.GetTargetTemperatureAsync(1), precision: 1);
    }

    [Fact]
    public async Task PlcBlackBodyAdapter_FailsWhenFEnetEndpointStops()
    {
        int port = GetFreeTcpPort();
        SimulatorSettings settings = SimulatorSettings.CreateDefaults() with
        {
            Endpoint = new EndpointSettings("127.0.0.1", port, "nats://127.0.0.1:4222"),
            OutputPath = Path.Combine(Path.GetTempPath(), "hcs_bb_fail_" + Guid.NewGuid().ToString("N"))
        };

        using var simulator = new FEnetPlcSimulator(settings);
        using var plc = new PlcXgtClient();
        simulator.Start();
        await plc.ConnectAsync("127.0.0.1", port);
        simulator.Stop();
        using var bb = new PlcBlackBodyAdapter(plc);

        await Assert.ThrowsAnyAsync<Exception>(() => bb.SetTemperatureAsync(0, 55.0f).WaitAsync(TimeSpan.FromSeconds(4)));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition was not met before the deadline.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
