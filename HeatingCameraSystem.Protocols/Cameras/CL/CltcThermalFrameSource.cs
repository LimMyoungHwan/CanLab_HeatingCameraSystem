using System;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// 실제 CLTC 열화상 프레임 소스: UVC 카메라를 raw Y16 모드(FourCC "Y16 ", ConvertRgb=0)로
    /// 열어 14비트로 마스킹된 열화상 프레임을 읽는다. <see cref="CltcLiveThermalCamera"/>에서
    /// 검증된 획득 로직을 그대로 따른다.
    /// </summary>
    public sealed class CltcThermalFrameSource : IThermalFrameSource
    {
        private readonly int _cameraIndex;
        private VideoCapture? _capture;
        private Mat? _mat;

        public CltcThermalFrameSource(int cameraIndex)
        {
            _cameraIndex = cameraIndex;
        }

        /// <summary>카메라를 연다. 이미 열려 있으면 무시하고, 열기에 실패하면 예외를 던진다.</summary>
        public void Open()
        {
            if (_capture is not null)
            {
                return;
            }

            var capture = new VideoCapture(_cameraIndex, VideoCaptureAPIs.DSHOW);
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('Y', '1', '6', ' '));
            capture.Set(VideoCaptureProperties.ConvertRgb, 0);

            if (!capture.IsOpened())
            {
                capture.Dispose();
                throw new InvalidOperationException($"Failed to open thermal camera index {_cameraIndex}.");
            }

            _capture = capture;
            _mat = new Mat();
        }

        /// <summary>
        /// 프레임 하나를 읽어 14비트로 마스킹해 반환한다. 미오픈·읽기 실패·형식 불일치는 예외 없이
        /// null을 반환하므로, 지속되면 CameraRuntime의 프레임 기아 감시가 Faulted로 떨어뜨린다.
        /// </summary>
        public ThermalFrame? Read()
        {
            var capture = _capture;
            var mat = _mat;
            if (capture is null || mat is null)
            {
                return null;
            }

            if (!capture.Read(mat) || mat.Empty())
            {
                return null;
            }

            if (mat.Type() != MatType.CV_16UC1)
            {
                return null;
            }

            int width = mat.Width;
            int height = mat.Height;
            var pixels = new ushort[width * height];

            Span<ushort> source = mat.AsSpan<ushort>();
            if (source.Length < pixels.Length)
            {
                return null;
            }

            source[..pixels.Length].CopyTo(pixels);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] &= 0x3FFF;
            }

            return new ThermalFrame(pixels, width, height, DateTimeOffset.Now);
        }

        public void Close()
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            _mat?.Dispose();
            _mat = null;
        }

        public void Dispose() => Close();
    }
}
