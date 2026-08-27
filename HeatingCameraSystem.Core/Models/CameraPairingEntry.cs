namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 카메라와 시리얼 포트 페어링 설정. <see cref="CameraUsbParentId"/>를 기준으로
    /// 어떤 COM 포트가 어떤 카메라에 연결되는지 저장한다.
    /// </summary>
    public sealed class CameraPairingEntry
    {
        /// <summary>카메라 USB 부모 장치 식별자. 페어링 조인 키.</summary>
        public string CameraUsbParentId { get; set; } = "";

        /// <summary>할당된 COM 포트 이름(예: COM3).</summary>
        public string PortName { get; set; } = "";

        /// <summary>카메라 시리얼 번호. null이면 아직 확인되지 않음.</summary>
        public string? ClSerialNumber { get; set; }

        /// <summary>표시용 이름. null이면 미지정.</summary>
        public string? DisplayName { get; set; }
    }
}
