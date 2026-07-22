using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
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
