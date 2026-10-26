using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.Tests;

public class CameraRuntimeLifecycleTests
{
    [Fact]
    public async Task FreshSnapshot_WhenFramesStop_ReturnsNullInsteadOfLastFrame()
    {
        using var source = new ControlledSource();
        using var runtime = new CameraRuntime(0, source, framePeriodMs: 5);
        var ready = new TaskCompletionSource<ThermalFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameReady += (_, frame) => ready.TrySetResult(frame);
        await runtime.StartAsync();
        ThermalFrame original = await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        ThermalFrame? snapshot = await runtime.CaptureSnapshotAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(40));

        Assert.Null(snapshot);
        Assert.Same(original, runtime.LatestFrame);
    }

    [Fact]
    public async Task ExternalSnapshotCancellation_IsNotConvertedToAFrameOrNull()
    {
        using var source = new ControlledSource();
        using var runtime = new CameraRuntime(0, source, framePeriodMs: 5);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameReady += (_, _) => ready.TrySetResult();
        await runtime.StartAsync();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();

        Task<ThermalFrame?> pending = runtime.CaptureSnapshotAsync(TimeSpan.Zero, TimeSpan.FromSeconds(2), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Stop_ClearsCachedFrame_AndCannotReturnItAsASnapshot()
    {
        using var source = new ControlledSource();
        using var runtime = new CameraRuntime(0, source, framePeriodMs: 5);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameReady += (_, _) => ready.TrySetResult();
        await runtime.StartAsync();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await runtime.StopAsync();

        Assert.Null(runtime.LatestFrame);
        Assert.Null(await runtime.CaptureSnapshotAsync());
    }

    [Fact]
    public async Task StartDuringStop_WaitsForOldReadAndClose_BeforeReopening()
    {
        using var source = new ControlledSource { HoldFirstRead = true };
        using var runtime = new CameraRuntime(0, source, framePeriodMs: 5);
        try
        {
            await runtime.StartAsync();
            await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task stopping = runtime.StopAsync();
            Task starting = runtime.StartAsync();
            Assert.False(stopping.IsCompleted);
            Assert.False(starting.IsCompleted);
            Assert.Equal(1, source.OpenCount);

            source.ReleaseRead.Set();
            await Task.WhenAll(stopping, starting).WaitAsync(TimeSpan.FromSeconds(2));
            await source.Reopened.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, source.OpenCount);
            Assert.Equal(1, source.MaximumConcurrentReads);
            Assert.False(source.ClosedDuringRead);
            Assert.True(runtime.IsRunning);
        }
        finally { source.ReleaseRead.Set(); }
    }

    [Fact]
    public async Task DisposeDuringStop_WaitsForTheSameLoop_AndForbidsRestart()
    {
        using var source = new ControlledSource { HoldFirstRead = true };
        var runtime = new CameraRuntime(0, source, framePeriodMs: 5);
        try
        {
            await runtime.StartAsync();
            await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task stopping = runtime.StopAsync();
            var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task disposing = Task.Run(() => { disposeStarted.TrySetResult(); runtime.Dispose(); });
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.WhenAny(disposing, Task.Delay(40));
            Assert.False(disposing.IsCompleted);
            Assert.Equal(0, source.DisposeCount);

            source.ReleaseRead.Set();
            await Task.WhenAll(stopping, disposing).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(source.DisposedDuringRead);
            Assert.Equal(1, source.DisposeCount);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.StartAsync());
        }
        finally
        {
            source.ReleaseRead.Set();
            runtime.Dispose();
        }
    }

    private sealed class ControlledSource : IThermalFrameSource
    {
        private int _reads;
        private int _activeReads;
        private int _maximumConcurrentReads;
        private int _openCount;
        private int _disposeCount;
        public bool HoldFirstRead { get; init; }
        public ManualResetEventSlim ReleaseRead { get; } = new();
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Reopened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int OpenCount => Volatile.Read(ref _openCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int MaximumConcurrentReads => Volatile.Read(ref _maximumConcurrentReads);
        public bool ClosedDuringRead { get; private set; }
        public bool DisposedDuringRead { get; private set; }

        public void Open()
        {
            if (Interlocked.Increment(ref _openCount) == 2) Reopened.TrySetResult();
        }
        public ThermalFrame? Read()
        {
            int active = Interlocked.Increment(ref _activeReads);
            int previous;
            do { previous = Volatile.Read(ref _maximumConcurrentReads); }
            while (active > previous && Interlocked.CompareExchange(ref _maximumConcurrentReads, active, previous) != previous);
            try
            {
                int number = Interlocked.Increment(ref _reads);
                ReadStarted.TrySetResult();
                if (number == 1 && HoldFirstRead) ReleaseRead.Wait();
                return number == 1 ? new ThermalFrame(new ushort[] { 8500, 8501, 8499, 8500 }, 2, 2, DateTimeOffset.Now) : null;
            }
            finally { Interlocked.Decrement(ref _activeReads); }
        }
        public void Close() => ClosedDuringRead |= Volatile.Read(ref _activeReads) != 0;
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposeCount, 1, 0) != 0) return;
            DisposedDuringRead = Volatile.Read(ref _activeReads) != 0;
            ReleaseRead.Set();
        }
    }
}
