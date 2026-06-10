using System;
using System.IO;
using OpenCvSharp;

namespace HeatingCameraSystem.Agent
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Initializing Heating Camera Agent (Phase 1)...");

            string storagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageStorage");
            using var cameraService = new HeatingCameraSystem.Agent.Services.CameraCaptureService(storagePath);

            // Open default camera (index 0)
            if (!cameraService.InitializeCamera(0))
            {
                Console.WriteLine("Error: Cannot open the camera.");
                return;
            }

            Console.WriteLine($"Camera opened successfully.");
            Console.WriteLine($"Saving images to: {storagePath}");
            Console.WriteLine("Press 'Q' or 'ESC' to stop capture.");

            int frameCount = 0;
            using var window = new Window("Camera Feed", WindowFlags.AutoSize);

            while (true)
            {
                if (!cameraService.CaptureFrame(out string savedFilePath))
                {
                    Console.WriteLine("Warning: Failed to capture or empty frame grabbed.");
                    break;
                }

                // Show frame
                using var frame = new Mat(savedFilePath);
                if (!frame.Empty())
                {
                    window.ShowImage(frame);
                }
                frameCount++;

                // Sleep for a short while (e.g., 1000ms for 1 fps as per 1초 간격 업데이트 요구사항 in spec)
                int key = Cv2.WaitKey(1000); 
                
                if (key == 27 || key == 'q' || key == 'Q') // ESC or Q
                {
                    break;
                }
            }

            Console.WriteLine($"Capture stopped. Total frames saved: {frameCount}");
        }
    }
}
