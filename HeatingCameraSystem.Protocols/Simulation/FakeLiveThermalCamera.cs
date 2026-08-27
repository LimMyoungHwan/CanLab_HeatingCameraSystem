using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 열화상 라이브 카메라. SimulationMode에서
    /// CltcLiveThermalCamera(OpenCV 캡처) 대신 사용한다.
    /// 백그라운드 루프가 67ms 간격(약 15Hz)으로 640x480 14-bit 합성 프레임을 발생시키며,
    /// 프레임 패턴은 <see cref="FakeThermalFrameSource"/>와 동일한 결정적 이동 핫스팟이다.
    /// cameraIndex는 무시되어 어떤 인덱스로 시작해도 같은 영상이 나온다.
    /// </summary>
    public class FakeLiveThermalCamera : ILiveThermalCamera
    {
        private const int Width = 640;
        private const int Height = 480;
        private readonly object _gate = new();
        private CancellationTokenSource? _runCts;
        private Task? _loopTask;
        private bool _isRunning;
        private int _tick;

        public event EventHandler<ThermalFrame>? FrameReady;

        public bool IsRunning
        {
            get { lock (_gate) return _isRunning; }
        }

        /// <summary>프레임 발생 루프를 시작한다. 이미 실행 중이면 아무것도 하지 않는다. cameraIndex는 사용하지 않는다.</summary>
        public Task StartAsync(int cameraIndex, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_isRunning)
                {
                    return Task.CompletedTask;
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _runCts = cts;
                _isRunning = true;
                _loopTask = Task.Run(() => RunLoopAsync(cts.Token));
            }

            return Task.CompletedTask;
        }

        /// <summary>루프를 취소하고 완전히 종료될 때까지 대기한다. 실행 중이 아니면 즉시 반환한다.</summary>
        public async Task StopAsync()
        {
            Task? loopTask;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                if (!_isRunning && _loopTask is null)
                {
                    return;
                }

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
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int tick = Interlocked.Increment(ref _tick);
                    FrameReady?.Invoke(this, CreateFrame(tick));
                    await Task.Delay(67, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _isRunning = false;
                }
            }
        }

        /// <summary>
        /// tick에서만 파생되는 결정적 합성 프레임을 만든다. 배경은 대각선 그라데이션,
        /// 그 위에 반지름 42px의 핫스팟이 tick마다 (11, 7)픽셀씩 이동한다. 값 범위는 0~0x3FFF(14-bit).
        /// </summary>
        private static ThermalFrame CreateFrame(int tick)
        {
            var pixels = new ushort[Width * Height];
            int centerX = tick * 11 % Width;
            int centerY = tick * 7 % Height;
            const int radius = 42;
            const int radiusSquared = radius * radius;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int value = (x * 7 + y * 5 + tick * 31) & 0x0FFF;
                    int dx = x - centerX;
                    int dy = y - centerY;
                    int distanceSquared = dx * dx + dy * dy;

                    if (distanceSquared <= radiusSquared)
                    {
                        value = 0x3FFF - distanceSquared * 0x1000 / radiusSquared;
                    }

                    pixels[y * Width + x] = (ushort)Math.Clamp(value, 0, 0x3FFF);
                }
            }

            return new ThermalFrame(pixels, Width, Height, DateTimeOffset.Now);
        }
    }
}
