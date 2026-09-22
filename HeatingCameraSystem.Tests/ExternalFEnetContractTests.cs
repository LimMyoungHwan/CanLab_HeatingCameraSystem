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
        private int _readIndividualRequests;

        public ConcurrentQueue<(DeviceType Device, uint Index, bool Value)> BitWrites { get; } = new();

        /// <summary>순수 비트 디바이스를 전부 ON으로 응답한다 — 인덱스 인코딩을 몰라도 검증할 수 있다.</summary>
        public bool AllBitsOn { get; set; }

        /// <summary>개별읽기 요청 수. 배칭이 실제로 왕복을 줄였는지 재는 값이다.</summary>
        public int ReadIndividualRequests => Volatile.Read(ref _readIndividualRequests);

        public void SetWord(uint index, short value) => _words[index] = value;

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
            Interlocked.Increment(ref _readIndividualRequests);

            foreach (var item in e.ResponseValues)
            {
                if (item.DeviceVariable.DeviceType == DeviceType.D && item.DeviceVariable.DataType == DataType.Word)
                    item.DeviceValue = new DeviceValue(_words.TryGetValue(item.DeviceVariable.Index, out var v) ? v : (short)0);
                else if (AllBitsOn && item.DeviceVariable.DataType == DataType.Bit)
                    item.DeviceValue = new DeviceValue(true);
            }
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
    public async Task PlcXgtClient_ReadStatusAsync_BatchesIndividualReads_AndMasksBitOfWord()
    {
        int port = GetFreeTcpPort();
        var memory = new WordMemory();

        var provider = new TcpChannelProvider(IPAddress.Loopback, port) { Logger = new NullChannelLogger() };
        var service = new FEnetSimulationService(provider) { UseHexBitIndex = true };
        var client = new PlcXgtClient();
        try
        {
            service.RequestedWriteIndividual += memory.OnWriteIndividual;
            service.RequestedReadIndividual += memory.OnReadIndividual;
            provider.Start();

            await client.ConnectAsync("127.0.0.1", port);

            memory.SetWord(100, 255);    // TempPv D100 → 25.5℃
            memory.SetWord(60, 1 << 1);  // StatusHeater(D60.1)만 ON, StatusCooler1st(D60.2)는 OFF
            memory.AllBitsOn = true;

            PlcStatusSnapshot s = await client.ReadStatusAsync();

            Assert.Equal(25.5f, s.CurrentTemperature, precision: 2);
            Assert.True(s.Heater);
            Assert.False(s.Cooler1st);
            Assert.True(s.Chiller);
            Assert.Equal(20, s.ErrorBits.Length);
            Assert.Equal(32, s.InputBits.Length);
            Assert.Equal(32, s.OutputBits.Length);
            Assert.All(s.OutputBits, bit => Assert.True(bit));

            // 변수 하나당 한 요청이면 126왕복이 되고, XGB 스캔 시간에서 1초 폴링 주기를 넘겨
            // PlcStatusService의 재진입 가드가 틱을 버려 화면이 2초마다 갱신된다.
            Assert.InRange(memory.ReadIndividualRequests, 1, 16);
        }
        finally
        {
            client.Dispose();
            service.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task PlcXgtClient_WriteGap_DoesNotBlockConcurrentRead()
    {
        int port = GetFreeTcpPort();
        var memory = new WordMemory();

        var provider = new TcpChannelProvider(IPAddress.Loopback, port) { Logger = new NullChannelLogger() };
        var service = new FEnetSimulationService(provider) { UseHexBitIndex = true };
        // 간격을 크게 잡아 "쓰기 간격 대기가 판독을 막는가"만 남긴다. 기본 100ms로는 잡음에 묻힌다.
        var client = new PlcXgtClient(new PlcSettings { WriteGapMs = 500 });
        try
        {
            service.RequestedWriteIndividual += memory.OnWriteIndividual;
            service.RequestedReadIndividual += memory.OnReadIndividual;
            provider.Start();

            await client.ConnectAsync("127.0.0.1", port);

            Task write = client.SetTargetTemperatureAsync(25.0f);
            await Task.Delay(150);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await client.GetCurrentTemperatureAsync();
            sw.Stop();

            await write;

            // 간격 대기를 IO 락 안에서 하면 판독이 남은 350ms를 통째로 기다린다.
            // 그 구조가 흑체 미러링(1초마다 쓰기)과 겹쳐 상태 폴링을 25초까지 밀어냈다.
            Assert.True(sw.ElapsedMilliseconds < 200, $"read waited {sw.ElapsedMilliseconds}ms behind the write gap");
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
