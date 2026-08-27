namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 단일 카메라의 단발 캡처를 담당한다. 초기화 후 프레임을 파일로 저장하고,
    /// 사용이 끝나면 연결을 해제한다. 실시간 스트리밍은 <see cref="ILiveThermalCamera"/>나
    /// <see cref="ICameraRuntime"/>을 쓴다.
    /// </summary>
    public interface ICameraCaptureService
    {
        /// <summary>지정한 카메라를 초기화한다. 이미 초기화된 상태면 재초기화한다.</summary>
        bool InitializeCamera(int cameraIndex);

        /// <summary>현재 프레임을 파일로 저장한다. 성공하면 savedFilePath에 전체 경로를 담는다.</summary>
        bool CaptureFrame(out string savedFilePath);

        /// <summary>카메라 사용을 중지하고 연결을 해제한다.</summary>
        void Stop();
    }
}
