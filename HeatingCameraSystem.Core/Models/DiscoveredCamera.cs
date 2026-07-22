namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// WMI / Fake 열거 결과 — 순간 스냅샷. 영속 레코드 아님.
    /// </summary>
    public class DiscoveredCamera
    {
        public string HardwareId    { get; set; } = string.Empty;   // WMI PnPDeviceID
        public string FriendlyName  { get; set; } = string.Empty;   // 표시용 이름
        public int    OpenCvIndex   { get; set; }                   // DirectShow 열거 순서
        public string UsbParentId   { get; set; } = string.Empty;   // USB 부모 id (카메라↔COM 페어링 조인 키)
    }

    /// <summary>PnP 이벤트 종류</summary>
    public enum PnpChangeType { Arrival, Removal }

    public class PnpChange
    {
        public PnpChangeType  ChangeType { get; set; }
        public DiscoveredCamera Camera   { get; set; } = new();
    }
}
