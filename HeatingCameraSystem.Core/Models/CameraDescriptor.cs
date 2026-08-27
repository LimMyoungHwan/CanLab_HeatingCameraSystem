namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// AgentUI 프로세스가 호스팅하는 로컬 카메라 하나의 신원과 물리 바인딩.
    /// <see cref="AgentId"/>는 안정적인 논리적 NATS 신원(AgentId 빌드 규칙에 따름)이고,
    /// <see cref="OpenCvIndex"/>는 물리적 OpenCV/DirectShow 장치 인덱스,
    /// <see cref="CameraSerialNumber"/>는 포트 독립적 애플리케이션 레벨 S/N,
    /// <see cref="UsbContainerId"/>는 같은 세션 내 포트 의존 폴백 키.
    /// </summary>
    public sealed record CameraDescriptor(string AgentId, int OpenCvIndex, string Alias, string? SerialPortName = null, string? DeviceName = null, string? CameraSerialNumber = null, string? UsbContainerId = null);
}
