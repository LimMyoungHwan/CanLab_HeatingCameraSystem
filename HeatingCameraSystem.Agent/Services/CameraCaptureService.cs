using System;
using System.IO;
using OpenCvSharp;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Agent.Services
{
    /// <summary>
    /// OpenCvSharp VideoCapture로 실제 USB 카메라에서 프레임을 잡아 JPEG로 저장하는 캡처 구현.
    /// SimulationMode가 아닐 때 사용된다.
    /// </summary>
    public class CameraCaptureService : ICameraCaptureService, IDisposable
    {
        private VideoCapture? _capture;
        private readonly string _storagePath;
        // [camera-model-select] Design Ref: §3.1 — 선택적 해상도. 미지정 시 기존 동작과 동일.
        private readonly int? _width;
        private readonly int? _height;

        public CameraCaptureService(string storagePath, int? width = null, int? height = null)
        {
            _storagePath = storagePath;
            _width = width;
            _height = height;
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        /// <summary>
        /// 지정 인덱스로 카메라를 연다. 모델 해상도가 지정된 경우에만 적용하며,
        /// 해상도 설정 실패는 경고만 남기고 카메라는 그대로 사용한다.
        /// </summary>
        public bool InitializeCamera(int cameraIndex)
        {
            try
            {
                _capture = new VideoCapture(cameraIndex);
                bool opened = _capture.IsOpened();

                // [camera-model-select] Design Ref: §3.2 — 모델 해상도 지정된 경우만 적용
                if (opened && _width.HasValue && _height.HasValue)
                {
                    try
                    {
                        _capture.FrameWidth = _width.Value;
                        _capture.FrameHeight = _height.Value;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: failed to set resolution {_width}x{_height}: {ex.Message}");
                    }
                }

                return opened;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing camera: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 프레임 한 장을 읽어 저장 폴더에 <c>capture_yyyyMMdd_HHmmss_fff.jpg</c>로 저장한다.
        /// 카메라가 열려 있지 않거나 빈 프레임이면 false를 반환한다.
        /// </summary>
        public bool CaptureFrame(out string savedFilePath)
        {
            savedFilePath = string.Empty;
            if (_capture == null || !_capture.IsOpened())
            {
                return false;
            }

            using var frame = new Mat();
            _capture.Read(frame);

            if (frame.Empty())
            {
                return false;
            }

            savedFilePath = Path.Combine(_storagePath, $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
            return frame.SaveImage(savedFilePath);
        }

        /// <summary>카메라 핸들을 해제한다. 이후 캡처는 실패를 반환한다.</summary>
        public void Stop()
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
