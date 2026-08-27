namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// USB-Serial COM 포트 열거 결과 — 순간 스냅샷. 영속 레코드 아님.
    /// <see cref="UsbParentId"/>는 나중에 카메라↔COM 페어링(공통 복합 부모 id 조인)에 사용된다.
    /// </summary>
    /// <param name="PortName">COM 포트 이름(예: COM7).</param>
    /// <param name="FriendlyName">표시용 이름(WMI Name).</param>
    /// <param name="HardwareId">하드웨어 식별자(WMI PNPDeviceID).</param>
    /// <param name="UsbParentId">공통 복합 부모 id. 페어링 조인 키.</param>
    public sealed record DiscoveredSerialPort(
        string PortName,
        string FriendlyName,
        string HardwareId,
        string UsbParentId);
}
