using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Cameras.CL;

namespace HeatingCameraSystem.Tests.Protocols;

public class ClSerialCameraClientTests
{
    [Theory]
    [InlineData(CameraBiasRegister.Cint)]
    [InlineData(CameraBiasRegister.TintMsb)]
    [InlineData(CameraBiasRegister.TintLsb)]
    [InlineData(CameraBiasRegister.GskMsb)]
    [InlineData(CameraBiasRegister.GskLsb)]
    [InlineData(CameraBiasRegister.Gfid)]
    public async Task DeviceErrorOnAnyBiasRegister_Throws(CameraBiasRegister register)
    {
        using var transport = new ScriptedTransport();
        transport.OnWrite = request => transport.Enqueue(Response(request, 0, result: 0xFF));
        using var client = new ClSerialCameraClient("test", transport, 50);
        await client.InitializeAsync();

        await Assert.ThrowsAsync<IOException>(() => client.SetBiasRegisterAsync(register, 0x73));
        Assert.Equal(1, transport.WriteCount);
    }

    [Fact]
    public async Task ReadErrorWithExpectedPayload_IsNotSuccessfulReadback()
    {
        using var transport = new ScriptedTransport();
        transport.OnWrite = request => transport.Enqueue(Response(request, 0x73, result: 0xFF));
        using var client = new ClSerialCameraClient("test", transport, 50);
        await client.InitializeAsync();

        await Assert.ThrowsAsync<IOException>(() => client.ReadBiasAsync());
    }

    [Fact]
    public async Task ForeignReplies_AreSkippedUntilMainSubAndRwAllMatch()
    {
        using var transport = new ScriptedTransport();
        transport.OnWrite = request =>
        {
            byte[] wrongMain = Response(request, 0x11); wrongMain[2] = 0x30;
            byte[] wrongSub = Response(request, 0x22); wrongSub[3] = 0x09;
            byte[] wrongRw = Response(request, 0x33); wrongRw[4] = 0x00;
            transport.Enqueue(wrongMain, wrongSub, wrongRw, Response(request, 0x73));
        };
        using var client = new ClSerialCameraClient("test", transport, 100);
        await client.InitializeAsync();

        Assert.Equal((byte)0x73, await client.ReadBiasAsync());
        Assert.Equal(1, transport.WriteCount);
    }

    [Fact]
    public async Task OneByteReadsAndLeadingNoise_RecoverTheFrameBoundary()
    {
        using var transport = new ScriptedTransport { MaxReadCount = 1 };
        transport.OnWrite = request => transport.Enqueue(new byte[] { 0x11, 0x43, 0x22 }, Response(request, 0x73));
        using var client = new ClSerialCameraClient("test", transport, 100);
        await client.InitializeAsync();

        Assert.Equal((byte)0x73, await client.ReadBiasAsync());
    }

    [Fact]
    public async Task PartialTimeout_ThenLatePreviousReply_CannotSatisfyTheNextRead()
    {
        using var transport = new ScriptedTransport();
        byte[] oldResponse = new byte[] { 0x43, 0x4C, 0x00, 0x06, 0x01, 0x00, 0x41 };
        transport.OnWrite = request => transport.Enqueue(transport.WriteCount == 1 ? oldResponse[..3] : Response(request, 0x73));
        using var client = new ClSerialCameraClient("test", transport, 30);
        await client.InitializeAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => client.ReadBiasAsync());

        // A fragment and a whole old reply arrive after the transaction timed out.
        transport.Enqueue(oldResponse[3..], oldResponse);
        // Another late reply arrives while the next transaction is waiting for quiet input.
        transport.OnRead = number => { if (number == 3) transport.Enqueue(oldResponse); };
        Assert.Equal((byte)0x73, await client.ReadBiasAsync());
        Assert.True(transport.DiscardCount >= 3);
    }

    [Fact]
    public async Task SlowPartialResponse_UsesOneTotalDeadline_NotAFullTimeoutPerByte()
    {
        using var transport = new ScriptedTransport { MaxReadCount = 1 };
        transport.OnWrite = request => transport.Enqueue(Response(request, 0x73));
        transport.OnRead = _ => Thread.Sleep(20);
        using var client = new ClSerialCameraClient("test", transport, 70);
        await client.InitializeAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => client.ReadBiasAsync());
        Assert.InRange(transport.ReadCount, 1, 4);
    }

    [Fact]
    public async Task CancellationDuringRead_IsPropagatedEvenIfAFullReplyArrives()
    {
        using var transport = new ScriptedTransport();
        using var client = new ClSerialCameraClient("test", transport, 200);
        await client.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        Task<byte> pending = client.ReadBiasAsync(cancellation.Token);
        await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        transport.Enqueue(new byte[] { 0x43, 0x4C, 0x00, 0x06, 0x01, 0x00, 0x73 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Dispose_CancelsAnActiveReadAndQueuedRead_BeforeDisposingTransport()
    {
        using var transport = new ScriptedTransport();
        var client = new ClSerialCameraClient("test", transport, 50);
        try
        {
            await client.InitializeAsync();
            Task<byte> active = client.ReadBiasAsync();
            await transport.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task<double> queued = client.ReadFpaTemperatureAsync();
            Assert.False(queued.IsCompleted);

            Task disposing = Task.Run(client.Dispose);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            await disposing.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(transport.DisposedDuringRead);
            Assert.Equal(1, transport.WriteCount);
            Assert.False(client.IsOpen);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.InitializeAsync());
        }
        finally { client.Dispose(); }
    }

    [Fact]
    public async Task Initialize_IsIdempotentAndHonorsPreCancellation()
    {
        using var transport = new ScriptedTransport();
        using var client = new ClSerialCameraClient("test", transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InitializeAsync(cancellation.Token));
        Assert.Equal(0, transport.OpenCount);

        await client.InitializeAsync();
        await client.InitializeAsync();
        Assert.Equal(1, transport.OpenCount);
    }

    [Fact]
    public async Task DeviceError_UsesFourAttemptBudget_AndNeverPersistsBias()
    {
        using var transport = new ScriptedTransport();
        transport.OnWrite = request => transport.Enqueue(Response(request, 0, result: 0xFF));
        using var client = new ClSerialCameraClient("test", transport, 20);
        await client.InitializeAsync();
        int writes = 0;

        BiasOperationResult result = await BiasOperationRunner.RunAsync(
            ct => client.SetBiasRegisterAsync(CameraBiasRegister.Cint, 0x93, ct),
            () => "{}", (_, _) => { writes++; return Task.FromResult("unexpected-bias.json"); },
            true, 2, "serial-device-error", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(4, transport.WriteCount);
        Assert.Equal(0, writes);
        Assert.Empty(result.FilePath);
    }

    private static byte[] Response(byte[] request, byte value, byte result = 0)
        => new byte[] { 0x43, 0x4C, request[2], request[3], request[4], result, value };

    private sealed class ScriptedTransport : IClSerialTransport
    {
        private readonly BlockingCollection<byte> _input = new();
        private int _readers;
        private int _disposed;
        public Action<byte[]>? OnWrite { get; set; }
        public Action<int>? OnRead { get; set; }
        public bool IsOpen { get; private set; }
        public int OpenCount { get; private set; }
        public int WriteCount { get; private set; }
        public int DiscardCount { get; private set; }
        public int ReadCount { get; private set; }
        public int MaxReadCount { get; set; } = int.MaxValue;
        public bool DisposedDuringRead { get; private set; }
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() { IsOpen = true; OpenCount++; }
        public void DiscardInBuffer()
        {
            DiscardCount++;
            while (_input.TryTake(out _)) { }
        }
        public void Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            WriteCount++;
            OnWrite?.Invoke(buffer.AsSpan(offset, count).ToArray());
        }
        public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            Interlocked.Increment(ref _readers);
            try
            {
                ReadStarted.TrySetResult();
                OnRead?.Invoke(++ReadCount);
                if (!_input.TryTake(out byte first, timeoutMilliseconds)) throw new TimeoutException();
                buffer[offset] = first;
                int read = 1;
                while (read < Math.Min(count, MaxReadCount) && _input.TryTake(out byte next))
                    buffer[offset + read++] = next;
                return read;
            }
            finally { Interlocked.Decrement(ref _readers); }
        }
        public void Enqueue(params byte[][] chunks)
        {
            foreach (byte[] chunk in chunks)
                foreach (byte value in chunk) _input.Add(value);
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            DisposedDuringRead = Volatile.Read(ref _readers) != 0;
            IsOpen = false;
            _input.Dispose();
        }
    }
}
