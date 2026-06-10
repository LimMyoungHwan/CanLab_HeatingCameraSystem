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

        public CameraCaptureService(string storagePath)
        {
            _storagePath = storagePath;
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
                return _capture.IsOpened();
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
