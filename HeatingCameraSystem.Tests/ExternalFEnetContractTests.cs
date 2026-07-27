using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;
using VagabondK.Protocols.Channels;
using VagabondK.Protocols.Logging;
using VagabondK.Protocols.LSElectric;
using VagabondK.Protocols.LSElectric.FEnet.Simulation;

namespace HeatingCameraSystem.Tests;

/// <summary>
/// Locks the external LS XGT FEnet simulation contract: a real <see cref="PlcXgtClient"/>
/// must write then read back a D-word over TCP loopback through the installed VagabondK
/// FEnet simulation service. Foundation for the standalone Simulator (later tasks build the
/// full device-memory map on top of this proven wiring).
/// </summary>
public class ExternalFEnetContractTests
{
    // ponytail: word-only store keyed by D index — enough to prove the contract;
    // the real device-memory map (byte arrays per DeviceType) is a later task.
    private sealed class WordMemory
    {
        private readonly ConcurrentDictionary<uint, short> _words = new();

        public void OnWriteIndividual(object? sender, FEnetRequestedWriteIndividualEventArgs e)
        {
            foreach (var kv in e.Values)
                if (kv.Key.DeviceType == DeviceType.D && kv.Key.DataType == DataType.Word)
                    _words[kv.Key.Index] = kv.Value.WordValue;
        }

        public void OnReadIndividual(object? sender, FEnetRequestedReadIndividualEventArgs e)
        {
            foreach (var item in e.ResponseValues)
                if (item.DeviceVariable.DeviceType == DeviceType.D && item.DeviceVariable.DataType == DataType.Word)
                    item.DeviceValue = new DeviceValue(_words.TryGetValue(item.DeviceVariable.Index, out var v) ? v : (short)0);
        }
    }

    // Load-bearing: the service NREs on a null channel.Logger before writing the response
    // (ResponseTimeout). Do not remove; the provider copies this onto each accepted channel.
    private sealed class NullChannelLogger : IChannelLogger
    {
        public void Log(ChannelLog log) { }
    }

    // ponytail: ask the OS for a free port, then reuse it — negligible TOCTOU for a loopback test.
    private static int GetFreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task PlcXgtClient_WritesThenReadsBack_DWord_ThroughPackageSimulationService()
    {
        int port = GetFreeTcpPort();
        var memory = new WordMemory();

        var provider = new TcpChannelProvider(IPAddress.Loopback, port) { Logger = new NullChannelLogger() };
        var service = new FEnetSimulationService(provider) { UseHexBitIndex = true };
        var client = new PlcXgtClient(new PlcSettings { ServoPointXBase = "D100" });
        try
        {
            service.RequestedWriteIndividual += memory.OnWriteIndividual;
            service.RequestedReadIndividual += memory.OnReadIndividual;
            provider.Start();

            await client.ConnectAsync("127.0.0.1", port);

            // Raw (unscaled) D-word round-trip: D100 = 1234, D102 = 5678.
            await client.SetPointCoordinateAsync(1, 1234, 5678);
            var (x, y) = await client.GetPointCoordinateAsync(1);

            Assert.Equal(1234, x);
            Assert.Equal(5678, y);
        }
        finally
        {
            client.Dispose();
            service.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public void TcpChannelProvider_Start_OnPortAlreadyInUse_ThrowsAddressInUse()
    {
        int port = GetFreeTcpPort();

        using var blocker = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        blocker.Start();

        using var provider = new TcpChannelProvider(IPAddress.Loopback, port);

        // TcpChannelProvider.Start() binds synchronously before its accept loop, so a taken
        // port fails deterministically with SocketException — not a hang, not a silent pass.
        var ex = Assert.Throws<SocketException>(() => provider.Start());
        Assert.Equal(SocketError.AddressAlreadyInUse, ex.SocketErrorCode);
    }
}
