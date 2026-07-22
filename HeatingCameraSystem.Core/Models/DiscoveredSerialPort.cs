namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// USB-Serial COM 포트 열거 결과 — 순간 스냅샷. 영속 레코드 아님.
    /// UsbParentId는 나중에 카메라↔COM 페어링(공통 복합 부모 id 조인)에 사용된다.
    /// </summary>
    public sealed record DiscoveredSerialPort(
        string PortName,        // "COM7"
        string FriendlyName,    // 표시용 이름 (WMI Name)
        string HardwareId,      // WMI PNPDeviceID
        string UsbParentId);    // 공통 복합 부모 id (페어링 조인 키)
}
