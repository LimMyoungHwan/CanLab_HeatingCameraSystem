using System;
using System.IO;
using HeatingCameraSystem.Core.Interfaces;
using OpenCvSharp;

namespace HeatingCameraSystem.Agent.Services
{
    public class FakeCameraCaptureService : ICameraCaptureService, IDisposable
    {
        private readonly string _storagePath;
        private readonly string _agentId;
        private int _cameraIndex = -1;
        private bool _initialized;

        public FakeCameraCaptureService(string storagePath, string agentId)
        {
            _storagePath = storagePath;
            _agentId     = agentId;
            if (!Directory.Exists(_storagePath))
                Directory.CreateDirectory(_storagePath);
        }

        public bool InitializeCamera(int cameraIndex)
        {
            _cameraIndex = cameraIndex;
            _initialized = true;
            Console.WriteLine($"[FakeCamera] InitializeCamera(idx={cameraIndex}) -> OK (synthetic)");
            return true;
        }

        public bool CaptureFrame(out string savedFilePath)
        {
            savedFilePath = string.Empty;
            if (!_initialized) return false;

            const int w = 640, h = 480;
            using var frame = new Mat(h, w, MatType.CV_8UC3, new Scalar(40, 40, 40));

            int hue = (int)((DateTime.Now.Millisecond / 1000.0) * 180);
            using var hsv = new Mat(h, w, MatType.CV_8UC3, new Scalar(hue, 200, 180));
            Cv2.CvtColor(hsv, frame, ColorConversionCodes.HSV2BGR);

            for (int i = 0; i < 5; i++)
                Cv2.Rectangle(frame,
                    new Rect(40 + i * 110, 80, 80, 80),
                    new Scalar(255 - i * 40, i * 50, 100 + i * 30),
                    thickness: -1);

            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Cv2.PutText(frame, $"SIM CAM {_cameraIndex}", new Point(40, 50),
                HersheyFonts.HersheySimplex, 1.2, new Scalar(255, 255, 255), 2);
            Cv2.PutText(frame, _agentId, new Point(40, 220),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 0), 2);
            Cv2.PutText(frame, ts, new Point(40, 260),
                HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 1);

            savedFilePath = Path.Combine(_storagePath,
                $"capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
            return frame.SaveImage(savedFilePath);
        }

        public void Stop() { _initialized = false; }

        public void Dispose() => Stop();
    }
}
