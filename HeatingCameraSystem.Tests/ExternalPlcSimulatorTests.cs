using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Plc;

namespace HeatingCameraSystem.Tests;

public class ExternalPlcSimulatorTests
{
    [Fact]
    public async Task PlcXgtClient_ObservesExternalSimulator_DynamicsAndServo()
    {
        int port = GetFreeTcpPort();
        SimulatorSettings settings = SimulatorSettings.CreateDefaults() with
        {
            Endpoint = new EndpointSettings("127.0.0.1", port, "nats://127.0.0.1:4222"),
            OutputPath = Path.Combine(Path.GetTempPath(), "hcs_plc_" + Guid.NewGuid().ToString("N"))
        };

        using var simulator = new FEnetPlcSimulator(settings);
        using var client = new PlcXgtClient();
        simulator.Start();
        await client.ConnectAsync("127.0.0.1", port);

        Assert.Equal(25.0f, await client.GetCurrentTemperatureAsync(), precision: 1);
        Assert.Equal(50.0f, await client.GetCurrentHumidityAsync(), precision: 1);

        await client.SetTargetTemperatureAsync(40.0f);
        await WaitUntilAsync(async () => await client.GetCurrentTemperatureAsync() >= 39.5f, TimeSpan.FromSeconds(2));

        await client.SetBlackBodyTemperatureAsync(0, 55.0f);
        await WaitUntilAsync(async () => await client.GetCurrentBlackBodyTemperatureAsync(0) >= 54.5f, TimeSpan.FromSeconds(2));

        await client.SetPointCoordinateAsync(1, 123, 456);
        await client.MoveServoToPositionAsync(1);
        await WaitUntilAsync(async () => (await client.ReadStatusAsync()).ServoXBusy, TimeSpan.FromSeconds(1));
        await WaitUntilAsync(async () => await client.IsServoAtPositionAsync(1), TimeSpan.FromSeconds(2));

        PlcStatusSnapshot moved = await client.ReadStatusAsync();
        Assert.Equal(123, moved.ServoXPosition);
        Assert.Equal(456, moved.ServoYPosition);

        await client.SetEquipmentAsync(PlcEquipment.Blower1, true);
        await client.SetFanSpeedAsync(37.5f);
        var admin = new PlcAdminSettings { OverheatLimit = 91.2f, CoolerDelayMinutes = 7 };
        await client.WriteAdminSettingsAsync(admin);
        simulator.Memory.WriteBitToken(new PlcSettings().ErrorBitBase, true);

        await WaitUntilAsync(async () => (await client.ReadStatusAsync()).Blower1, TimeSpan.FromSeconds(1));
        PlcStatusSnapshot status = await client.ReadStatusAsync();
        Assert.Equal(37.5f, status.FanSpeedHz, precision: 1);
        Assert.Equal(91.2f, status.Admin.OverheatLimit, precision: 1);
        Assert.Equal(7, status.Admin.CoolerDelayMinutes);
        Assert.True(status.ErrorBits[0]);
    }

    [Fact]
    public async Task OfflineThenRestart_FailsFast_ThenRecovers()
    {
        int port = GetFreeTcpPort();
        SimulatorSettings settings = SimulatorSettings.CreateDefaults() with
        {
            Endpoint = new EndpointSettings("127.0.0.1", port, "nats://127.0.0.1:4222"),
            OutputPath = Path.Combine(Path.GetTempPath(), "hcs_plc_" + Guid.NewGuid().ToString("N"))
        };

        using var simulator = new FEnetPlcSimulator(settings);
        using var client = new PlcXgtClient();
        simulator.Start();
        await client.ConnectAsync("127.0.0.1", port);
        simulator.Stop();

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetCurrentTemperatureAsync().WaitAsync(TimeSpan.FromSeconds(4)));

        simulator.Start();
        using var recovered = new PlcXgtClient();
        await recovered.ConnectAsync("127.0.0.1", port);
        Assert.Equal(25.0f, await recovered.GetCurrentTemperatureAsync(), precision: 1);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition()) return;
            await Task.Delay(50, CancellationToken.None);
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
