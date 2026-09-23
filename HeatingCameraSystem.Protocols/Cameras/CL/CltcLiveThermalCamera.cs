using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// CLTC 카메라의 라이브 스트림 구현: raw Y16 모드로 열고 자체 캡처 루프에서 프레임마다
    /// 33ms 지연을 두고 <see cref="FrameReady"/>를 발행한다. 프레임 변환은
    /// <see cref="CltcThermalFrameSource"/>와 같은 <see cref="ClThermalMatDecoder"/>를 쓴다.
    /// </summary>
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

        /// <summary>캡처 루프를 시작한다. 이미 실행 중이면 무시하고, 카메라 열기에 실패하면 예외를 던진다.</summary>
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
            ClCaptureSetup.OpenRawY16(capture);

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

        /// <summary>루프에 취소를 걸고 종료를 기다린 뒤 캡처 자원을 해제한다.</summary>
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

                    ThermalFrame? decoded = ClThermalMatDecoder.Decode(frame, DateTimeOffset.Now);
                    if (decoded is null)
                    {
                        continue;
                    }

                    FrameReady?.Invoke(this, decoded);
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

    }
}
