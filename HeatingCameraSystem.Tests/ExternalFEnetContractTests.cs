using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Models;
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

        public ConcurrentQueue<(DeviceType Device, uint Index, bool Value)> BitWrites { get; } = new();

        public void OnWriteIndividual(object? sender, FEnetRequestedWriteIndividualEventArgs e)
        {
            foreach (var kv in e.Values)
            {
                if (kv.Key.DeviceType == DeviceType.D && kv.Key.DataType == DataType.Word)
                    _words[kv.Key.Index] = kv.Value.WordValue;
                else if (kv.Key.DataType == DataType.Bit)
                    BitWrites.Enqueue((kv.Key.DeviceType, kv.Key.Index, kv.Value.BitValue));
            }
        }

        public void OnReadIndividual(object? sender, FEnetRequestedReadIndividualEventArgs e)
        {
            foreach (var item in e.ResponseValues)
                if (item.DeviceVariable.DeviceType == DeviceType.D && item.DeviceVariable.DataType == DataType.Word)
                    item.DeviceValue = new DeviceValue(_words.TryGetValue(item.DeviceVariable.Index, out var v) ? v : (short)0);
        }

        public short ReadWord(uint index) => _words.TryGetValue(index, out var v) ? v : (short)0;
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

            // mm 값은 0.1mm 단위 워드로 스케일링된다: D100 = 1234, D102 = 5678.
            await client.SetPointCoordinateAsync(1, 123.4f, 567.8f);
            var (x, y) = await client.GetPointCoordinateAsync(1);

            Assert.Equal(123.4f, x, precision: 2);
            Assert.Equal(567.8f, y, precision: 2);
            Assert.Equal(1234, memory.ReadWord(100));
            Assert.Equal(5678, memory.ReadWord(102));
        }
        finally
        {
            client.Dispose();
            service.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task PlcXgtClient_PBitTrigger_PulsesOnThenOff_AndJogHolds()
    {
        int port = GetFreeTcpPort();
        var memory = new WordMemory();

        var provider = new TcpChannelProvider(IPAddress.Loopback, port) { Logger = new NullChannelLogger() };
        var service = new FEnetSimulationService(provider) { UseHexBitIndex = true };
        var client = new PlcXgtClient(new PlcSettings { PulseHoldMs = 30 });
        try
        {
            service.RequestedWriteIndividual += memory.OnWriteIndividual;
            service.RequestedReadIndividual += memory.OnReadIndividual;
            provider.Start();

            await client.ConnectAsync("127.0.0.1", port);

            // P601 포인트 이동 트리거 → ON, PulseHoldMs 후 OFF (모멘터리).
            await client.MoveServoToPositionAsync(1);
            var pulse = memory.BitWrites.Where(w => w.Device == DeviceType.P).ToArray();
            Assert.Equal(2, pulse.Length);
            Assert.True(pulse[0].Value);
            Assert.False(pulse[1].Value);
            Assert.Equal(pulse[0].Index, pulse[1].Index);

            // JOG는 유지 동작 → 누름은 ON 한 번만, 뗌에서 OFF.
            memory.BitWrites.Clear();
            await client.JogAsync(ServoAxis.X, positive: true, on: true);
            Assert.Single(memory.BitWrites);
            Assert.True(memory.BitWrites.Single().Value);

            // M 영역 비트는 펄스 대상 아님 → ON 한 번만.
            memory.BitWrites.Clear();
            await client.SetEquipmentAsync(PlcEquipment.Blower1, true);
            Assert.Single(memory.BitWrites);
            Assert.True(memory.BitWrites.Single().Value);

            // 부저 OFF(P250) / 에러 리셋(P525)도 모멘터리.
            foreach (Func<Task> trigger in new Func<Task>[] { client.BuzzerOffAsync, client.ResetErrorAsync })
            {
                memory.BitWrites.Clear();
                await trigger();
                var writes = memory.BitWrites.ToArray();
                Assert.Equal(2, writes.Length);
                Assert.True(writes[0].Value);
                Assert.False(writes[1].Value);
                Assert.Equal(writes[0].Index, writes[1].Index);
            }
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
