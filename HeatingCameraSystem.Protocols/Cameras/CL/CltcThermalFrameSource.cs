using System;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// Real CLTC thermal frame source: opens the UVC camera in raw Y16 mode
    /// (FourCC "Y16 ", ConvertRgb=0) and reads 14-bit masked thermal frames.
    /// Mirrors the proven acquisition logic in <see cref="CltcLiveThermalCamera"/>.
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
