using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    public class CltcLiveThermalCamera : ILiveThermalCamera
    {
        private readonly object _gate = new();
        private VideoCapture? _capture;
        private CancellationTokenSource? _runCts;
        private Task? _loopTask;
        private bool _isRunning;

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
            }

            var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('Y', '1', '6', ' '));
            capture.Set(VideoCaptureProperties.ConvertRgb, 0);

            if (!capture.IsOpened())
            {
                capture.Dispose();
                throw new InvalidOperationException($"Failed to open thermal camera index {cameraIndex}.");
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            lock (_gate)
            {
                if (_isRunning)
                {
                    cts.Dispose();
                    capture.Release();
                    capture.Dispose();
                    return Task.CompletedTask;
                }

                _capture = capture;
                _runCts = cts;
                _isRunning = true;
                _loopTask = Task.Run(() => CaptureLoopAsync(capture, cts.Token));
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            Task? loopTask;
            VideoCapture? capture;
            CancellationTokenSource? cts;

            lock (_gate)
            {
                if (!_isRunning && _loopTask is null)
                {
                    return;
                }

                _isRunning = false;
                loopTask = _loopTask;
                capture = _capture;
                cts = _runCts;
                _loopTask = null;
                _capture = null;
                _runCts = null;
                cts?.Cancel();
            }

            if (loopTask is not null)
            {
                await loopTask.ConfigureAwait(false);
            }

            capture?.Release();
            capture?.Dispose();
            cts?.Dispose();
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        private async Task CaptureLoopAsync(VideoCapture capture, CancellationToken token)
        {
            try
            {
                using var frame = new Mat();
                while (!token.IsCancellationRequested)
                {
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        await Task.Delay(5, token).ConfigureAwait(false);
                        continue;
                    }

                    if (frame.Type() != MatType.CV_16UC1)
                    {
                        continue;
                    }

                    int width = frame.Width;
                    int height = frame.Height;
                    var pixels = new ushort[width * height];

                    if (!TryCopyMaskedPixels(frame, pixels))
                    {
                        continue;
                    }

                    FrameReady?.Invoke(this, new ThermalFrame(pixels, width, height, DateTimeOffset.Now));
                    await Task.Delay(33, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_capture, capture))
                    {
                        _isRunning = false;
                    }
                }
            }
        }

        private static bool TryCopyMaskedPixels(Mat frame, ushort[] pixels)
        {
            Span<ushort> source = frame.AsSpan<ushort>();
            if (source.Length < pixels.Length)
            {
                return false;
            }

            source[..pixels.Length].CopyTo(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] &= 0x3FFF;
            }

            return true;
        }
    }
}
