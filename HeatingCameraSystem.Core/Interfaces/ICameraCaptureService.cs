namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICameraCaptureService
    {
        bool InitializeCamera(int cameraIndex);
        bool CaptureFrame(out string savedFilePath);
        void Stop();
    }
}
