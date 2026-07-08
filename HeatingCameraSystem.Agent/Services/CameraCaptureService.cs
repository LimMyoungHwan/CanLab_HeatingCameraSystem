using System;
using System.IO;
using OpenCvSharp;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Agent.Services
{
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
