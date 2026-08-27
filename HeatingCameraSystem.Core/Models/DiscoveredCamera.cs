namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// WMI / Fake 열거 결과 — 순간 스냅샷. 영속 레코드 아님.
    /// </summary>
    public class DiscoveredCamera
    {
        /// <summary>장치 하드웨어 식별자(WMI PnPDeviceID).</summary>
        public string HardwareId    { get; set; } = string.Empty;

        /// <summary>표시용 이름.</summary>
        public string FriendlyName  { get; set; } = string.Empty;

        /// <summary>DirectShow 열거 순서.</summary>
        public int    OpenCvIndex   { get; set; }

        /// <summary>USB 부모 id. 카메라↔COM 페어링 조인 키.</summary>
        public string UsbParentId   { get; set; } = string.Empty;
    }

    /// <summary>PnP 이벤트 종류.</summary>
    public enum PnpChangeType
    {
        /// <summary>장치 연결.</summary>
        Arrival,

        /// <summary>장치 제거.</summary>
        Removal
    }

    /// <summary>PnP 장치 변경 이벤트.</summary>
    public class PnpChange
    {
        /// <summary>변경 종류.</summary>
        public PnpChangeType  ChangeType { get; set; }

        /// <summary>대상 카메라.</summary>
        public DiscoveredCamera Camera   { get; set; } = new();
    }
}
