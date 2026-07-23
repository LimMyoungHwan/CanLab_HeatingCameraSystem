using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Generic per-camera runtime: owns one <see cref="IThermalFrameSource"/>, runs a single
    /// continuous read loop, publishes frames to live subscribers, and keeps the latest frame
    /// for capture-by-tee. Frame acquisition is delegated to the source, so the runtime logic
    /// is hardware-independent (and unit-testable with a fake source).
    /// </summary>
    public sealed class CameraRuntime : ICameraRuntime
    {
        private static readonly TimeSpan DefaultNextFrameTimeout = TimeSpan.FromSeconds(2);

        private readonly object _gate = new();
        private readonly IThermalFrameSource _source;
        private readonly int _framePeriodMs;

        private CancellationTokenSource? _runCts;
        private Task? _loopTask;
        private bool _isRunning;
        private ThermalFrame? _latest;
        private CameraRuntimeStatus _status = CameraRuntimeStatus.Stopped;

        public CameraRuntime(int cameraIndex, IThermalFrameSource source, int framePeriodMs = 33)
        {
            CameraIndex = cameraIndex;
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _framePeriodMs = framePeriodMs > 0 ? framePeriodMs : 33;
        }

        public int CameraIndex { get; }

        public CameraRuntimeStatus Status
        {
            get { lock (_gate) return _status; }
        }

        public bool IsRunning
        {
            get { lock (_gate) return _isRunning; }
        }

        public ThermalFrame? LatestFrame => Volatile.Read(ref _latest);

        public event EventHandler<ThermalFrame>? FrameReady;
        public event EventHandler<CameraRuntimeStatus>? StatusChanged;

        public Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_isRunning) return Task.CompletedTask;

                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _runCts = cts;
                _isRunning = true;
                _loopTask = Task.Run(() => RunLoopAsync(cts.Token));
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            Task? loopTask;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                if (!_isRunning && _loopTask is null) return;

                _isRunning = false;
                loopTask = _loopTask;
                cts = _runCts;
                _loopTask = null;
                _runCts = null;
                cts?.Cancel();
            }

            if (loopTask is not null)
            {
                await loopTask.ConfigureAwait(false);
            }

            cts?.Dispose();
            SetStatus(CameraRuntimeStatus.Stopped);
        }

        public async Task<ThermalFrame?> CaptureSnapshotAsync(
            TimeSpan? maxAge = null,
            TimeSpan? nextFrameTimeout = null,
            CancellationToken ct = default)
        {
            var latest = LatestFrame;
            if (latest is not null &&
                (maxAge is null || DateTimeOffset.Now - latest.Timestamp <= maxAge.Value))
            {
                return latest;
            }

            // Need a fresher frame: tee the next one from the live loop.
            var tcs = new TaskCompletionSource<ThermalFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? _, ThermalFrame f) => tcs.TrySetResult(f);

            FrameReady += Handler;
            try
            {
                if (!IsRunning && LatestFrame is null)
                {
                    return latest; // not producing; return whatever we had (possibly null)
                }

                var timeout = nextFrameTimeout ?? DefaultNextFrameTimeout;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);

                using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
                {
                    try
                    {
                        return await tcs.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // stalled / timed out — fall back to last known frame (may be null)
                        return LatestFrame ?? latest;
                    }
                }
            }
            finally
            {
                FrameReady -= Handler;
            }
        }

        public void Dispose()
        {
            try { StopAsync().GetAwaiter().GetResult(); }
            catch { /* best effort */ }
            _source.Dispose();
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                _source.Open();
                SetStatus(CameraRuntimeStatus.Running);

                while (!token.IsCancellationRequested)
                {
                    ThermalFrame? frame;
                    try
                    {
                        frame = _source.Read();
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        SetStatus(CameraRuntimeStatus.Faulted);
                        await Task.Delay(200, token).ConfigureAwait(false);
                        continue;
                    }

                    if (frame is null)
                    {
                        await Task.Delay(5, token).ConfigureAwait(false);
                        continue;
                    }

                    if (Status == CameraRuntimeStatus.Faulted)
                    {
                        SetStatus(CameraRuntimeStatus.Running);
                    }

                    Volatile.Write(ref _latest, frame);
                    FrameReady?.Invoke(this, frame);

                    await Task.Delay(_framePeriodMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                SetStatus(CameraRuntimeStatus.Faulted);
            }
            finally
            {
                try { _source.Close(); }
                catch { /* best effort */ }
                lock (_gate) { _isRunning = false; }
            }
        }

        private void SetStatus(CameraRuntimeStatus status)
        {
            bool changed;
            lock (_gate)
            {
                changed = _status != status;
                _status = status;
            }

            if (changed)
            {
                StatusChanged?.Invoke(this, status);
            }
        }
    }
}
