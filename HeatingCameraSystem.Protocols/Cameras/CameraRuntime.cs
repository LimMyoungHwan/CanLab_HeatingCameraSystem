using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 카메라 한 대를 담당하는 범용 런타임: <see cref="IThermalFrameSource"/> 하나를 소유하고,
    /// 단일 연속 읽기 루프를 돌리며, 라이브 구독자에게 프레임을 발행하고, capture-by-tee용으로
    /// 최신 프레임을 보관한다. 프레임 획득은 소스에 위임하므로 런타임 로직은 하드웨어와 무관하다
    /// (가짜 소스로 단위 테스트 가능).
    /// </summary>
    public sealed class CameraRuntime : ICameraRuntime
    {
        private static readonly TimeSpan DefaultNextFrameTimeout = TimeSpan.FromSeconds(2);

        private readonly object _gate = new();
        private readonly SemaphoreSlim _lifecycle = new(1, 1);
        private readonly IThermalFrameSource _source;
        private readonly int _framePeriodMs;
        private readonly int _frameTimeoutMs;

        private CancellationTokenSource? _runCts;
        private Task? _loopTask;
        private bool _isRunning;
        private bool _stopping;
        private int _disposeRequested;
        private bool _sourceDisposed;
        private ThermalFrame? _latest;
        private CameraRuntimeStatus _status = CameraRuntimeStatus.Stopped;

        public CameraRuntime(int cameraIndex, IThermalFrameSource source, int framePeriodMs = 33, int frameTimeoutMs = 3000)
        {
            CameraIndex = cameraIndex;
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _framePeriodMs = framePeriodMs > 0 ? framePeriodMs : 33;
            // ponytail: 3초는 실측 없이 고른 값 — USB 재협상이나 느린 센서 워밍업에서 오탐이 나면 올릴 것.
            _frameTimeoutMs = frameTimeoutMs > 0 ? frameTimeoutMs : 3000;
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

        /// <summary>읽기 루프를 시작한다. 이미 실행 중이면 아무것도 하지 않는다.</summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
                Task? previous;
                lock (_gate)
                {
                    if (_isRunning) return;
                    previous = _loopTask;
                }
                // A faulted/completed loop still owns its source until its finally has finished.
                if (previous is not null) await previous.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
                _runCts?.Dispose();

                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lock (_gate)
                {
                    _runCts = cts;
                    _stopping = false;
                    _isRunning = true;
                    Volatile.Write(ref _latest, null);
                    _loopTask = Task.Run(() => RunLoopAsync(cts.Token));
                }
            }
            finally { _lifecycle.Release(); }
        }

        /// <summary>루프에 취소를 걸고 종료를 기다린 뒤 상태를 Stopped로 만든다.</summary>
        public async Task StopAsync()
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try { await StopCoreAsync().ConfigureAwait(false); }
            finally { _lifecycle.Release(); }
        }

        private async Task StopCoreAsync()
        {
            Task? loopTask;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                _stopping = true;
                loopTask = _loopTask;
                cts = _runCts;
                Volatile.Write(ref _latest, null);
            }
            cts?.Cancel();

            if (loopTask is not null)
            {
                await loopTask.ConfigureAwait(false);
            }

            lock (_gate)
            {
                _loopTask = null;
                _runCts = null;
                _isRunning = false;
                _stopping = false;
            }
            cts?.Dispose();
            SetStatus(CameraRuntimeStatus.Stopped);
        }

        /// <summary>
        /// maxAge 안의 최신 프레임이 있으면 즉시 반환하고, 아니면 라이브 루프의 다음 프레임을
        /// 가로채 기다린다. 시간 안에 조건을 만족하는 프레임이 없으면 null을 반환하며,
        /// 외부 취소는 전파한다. 과거 프레임을 새 촬영으로 돌려주지 않는다.
        /// </summary>
        public async Task<ThermalFrame?> CaptureSnapshotAsync(
            TimeSpan? maxAge = null,
            TimeSpan? nextFrameTimeout = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DateTimeOffset requestedAt = DateTimeOffset.Now;
            bool Fresh(ThermalFrame frame) => maxAge is null
                || (maxAge == TimeSpan.Zero ? frame.Timestamp > requestedAt
                    : DateTimeOffset.Now - frame.Timestamp <= maxAge.Value);
            if (!CanCapture()) return null;
            ThermalFrame? latest = LatestFrame;
            if (Status == CameraRuntimeStatus.Running && latest is not null && Fresh(latest)) return latest;

            // 더 신선한 프레임이 필요: 라이브 루프에서 다음 프레임을 가로챈다.
            var tcs = new TaskCompletionSource<ThermalFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? _, ThermalFrame f)
            {
                if (CanCapture() && Fresh(f)) tcs.TrySetResult(f);
            }

            FrameReady += Handler;
            try
            {
                // Cover a frame published between the first cache read and subscribing.
                latest = LatestFrame;
                if (!CanCapture()) return null;
                if (Status == CameraRuntimeStatus.Running && latest is not null && Fresh(latest)) return latest;
                var timeout = nextFrameTimeout ?? DefaultNextFrameTimeout;
                try
                {
                    ThermalFrame frame = await tcs.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    return CanCapture() ? frame : null;
                }
                catch (TimeoutException) { return null; }
            }
            finally
            {
                FrameReady -= Handler;
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposeRequested, 1);
            DisposeCoreAsync().GetAwaiter().GetResult();
        }

        private async Task DisposeCoreAsync()
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_sourceDisposed) return;
                await StopCoreAsync().ConfigureAwait(false);
                _sourceDisposed = true;
                _source.Dispose();
            }
            finally { _lifecycle.Release(); }
        }

        private bool CanCapture()
        {
            lock (_gate)
                return _isRunning && !_stopping && Volatile.Read(ref _disposeRequested) == 0;
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                _source.Open();
                SetStatus(CameraRuntimeStatus.Running);
                long lastFrameTicks = Stopwatch.GetTimestamp();

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

                    token.ThrowIfCancellationRequested();

                    if (frame is null)
                    {
                        // 프레임 기아: Read가 예외 없이 계속 null만 주면 영상은 죽었는데 상태는
                        // Running으로 남아 Master가 정상으로 표시한다. 유한 시간 뒤 Faulted로 떨어뜨린다.
                        if (ElapsedMs(lastFrameTicks) >= _frameTimeoutMs)
                        {
                            SetStatus(CameraRuntimeStatus.Faulted);
                        }
                        await Task.Delay(5, token).ConfigureAwait(false);
                        continue;
                    }

                    lastFrameTicks = Stopwatch.GetTimestamp();
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
                catch { /* 최선 노력 */ }
                lock (_gate)
                {
                    _isRunning = false;
                    Volatile.Write(ref _latest, null);
                }
            }
        }

        private static double ElapsedMs(long sinceTicks)
            => (Stopwatch.GetTimestamp() - sinceTicks) * 1000.0 / Stopwatch.Frequency;

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
