using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Simulator.Config;
using HeatingCameraSystem.Simulator.Plc;
using Moq;

namespace HeatingCameraSystem.Tests;

public class BlackBodyRoutingTests
{
    [Fact]
    public void AppServices_SelectsDirectControllerOutsideSimulationMode()
    {
        using var plc = new FakePlcController();

        using var fake = AppServices.CreateBlackBodyController(new HardwareSettings { SimulationMode = true }, plc);
        using var real = AppServices.CreateBlackBodyController(new HardwareSettings { SimulationMode = false }, plc);

        Assert.IsType<FakeBlackBodyController>(fake);
        Assert.IsType<SrBlackBodyController>(real);
    }

    [Fact]
    public void AppServices_SelectsSrControllerWhenBlackBodyEnabled()
    {
        using var plc = new FakePlcController();
        var settings = new HardwareSettings { SimulationMode = false };
        settings.BlackBody.Enabled = true;

        using var bb = AppServices.CreateBlackBodyController(settings, plc);

        Assert.IsType<SrBlackBodyController>(bb);
        Assert.Equal(2, bb.Count);
    }

    [Fact]
    public async Task SrController_WritesSetpointToBlackBodyAndPlc()
    {
        var plc = new Mock<IPlcController>();
        var settings = new BlackBodySettings
        {
            Simulated = true,
            Units = [new BlackBodyUnitSettings()]
        };
        using var blackBody = new SrBlackBodyController(settings, plc: plc.Object);
        await blackBody.ConnectAsync();

        await blackBody.SetTemperatureAsync(0, 35f);

        Assert.Equal(35f, await blackBody.GetTargetTemperatureAsync(0));
        plc.Verify(x => x.SetBlackBodyTemperatureAsync(0, 35f), Times.Once);
    }

    [Fact]
    public async Task UdpSrLink_WritesAndReadsCompleteFrame()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        byte[] response = { SrProtocol.Sync, 1, 0, 2, 0x12, 0x34 };
        Task serverTask = Task.Run(async () =>
        {
            UdpReceiveResult request = await server.ReceiveAsync();
            await server.SendAsync(response, request.RemoteEndPoint);
        });

        using var link = new UdpSrLink(new BlackBodyUnitSettings
        {
            ConnectionType = BlackBodyConnectionType.Ip,
            IpAddress = "127.0.0.1",
            Port = port
        }, 1500);
        link.Open();
        link.Write(new byte[] { 0xAA, 0xBB });

        Assert.Equal(response, link.Read());
        await serverTask;
    }

    [Fact]
    public async Task PlcStatusService_WritesDirectBlackBodyValuesToPlc()
    {
        var plc = new Mock<IPlcController>();
        plc.Setup(x => x.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot());
        plc.Setup(x => x.WriteBlackBodyTemperaturesAsync(It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Task.CompletedTask);
        var blackBody = new Mock<IBlackBodyController>();
        blackBody.SetupGet(x => x.Count).Returns(2);
        blackBody.Setup(x => x.GetCurrentTemperatureAsync(0)).ReturnsAsync(30.1f);
        blackBody.Setup(x => x.GetTargetTemperatureAsync(0)).ReturnsAsync(35f);
        blackBody.Setup(x => x.GetCurrentTemperatureAsync(1)).ReturnsAsync(40.2f);
        blackBody.Setup(x => x.GetTargetTemperatureAsync(1)).ReturnsAsync(45f);
        var service = new PlcStatusService(plc.Object, blackBody.Object);

        await service.RefreshAsync();

        plc.Verify(x => x.WriteBlackBodyTemperaturesAsync(0, 30.1f, 35f), Times.Once);
        plc.Verify(x => x.WriteBlackBodyTemperaturesAsync(1, 40.2f, 45f), Times.Once);
        Assert.Equal(30.1f, service.Snapshot.BlackBody1Pv);
        Assert.Equal(35f, service.Snapshot.BlackBody1Sv);
        Assert.Equal(40.2f, service.Snapshot.BlackBody2Pv);
        Assert.Equal(45f, service.Snapshot.BlackBody2Sv);
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
