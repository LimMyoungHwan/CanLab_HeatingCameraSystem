namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 발견된 카메라와 COM 포트의 페어링 후보. <see cref="DiscoveredCamera"/>와
    /// <see cref="DiscoveredSerialPort"/>를 조인한 상태와 페어링 가능성을 표현한다.
    /// </summary>
    /// <param name="Camera">발견된 카메라.</param>
    /// <param name="SerialPort">후보 COM 포트. null이면 시리얼 미탐지.</param>
    /// <param name="CameraSerialNumber">확정된 카메라 시리얼 번호. null이면 미확인.</param>
    /// <param name="Status">페어링 상태.</param>
    /// <param name="IsManualOverride">운영자가 수동으로 매칭한 경우 true.</param>
    public sealed record CameraComPair(
        DiscoveredCamera Camera,
        DiscoveredSerialPort? SerialPort,
        string? CameraSerialNumber,
        PairingStatus Status,
        bool IsManualOverride);
}
