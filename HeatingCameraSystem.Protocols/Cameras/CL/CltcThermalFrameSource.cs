using System;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>
    /// 실제 CLTC 열화상 프레임 소스: UVC 카메라를 raw Y16 모드(FourCC "Y16 ", ConvertRgb=0)로
    /// 열고 <see cref="ClThermalMatDecoder"/>로 프레임을 변환한다. 제품이 UYVY 모드면 Y16 요청과
    /// 무관하게 8비트 프레임이 오므로 변환은 디코더가 판별한다.
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
            ClCaptureSetup.OpenRawY16(capture);

            if (!capture.IsOpened())
            {
                capture.Dispose();
                throw new InvalidOperationException($"Failed to open thermal camera index {_cameraIndex}.");
            }

            _capture = capture;
            _mat = new Mat();
        }

        /// <summary>
        /// 프레임 하나를 읽어 <see cref="ClThermalMatDecoder"/>로 변환해 반환한다. 미오픈·읽기 실패·
        /// 형식 불일치는 예외 없이 null을 반환하므로, 지속되면 CameraRuntime의 프레임 기아 감시가
        /// Faulted로 떨어뜨린다.
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

            return ClThermalMatDecoder.Decode(mat, DateTimeOffset.Now);
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
