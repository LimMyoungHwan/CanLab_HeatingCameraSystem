namespace HeatingCameraSystem.Core.Models
{
    /// <summary>카메라 셔터용 시리얼 포트 설정.</summary>
    public class CameraSerialSettings
    {
        /// <summary>Agent 내 카메라 인덱스. 설정이 적용될 카메라를 식별한다.</summary>
        public int CameraIndex { get; set; }

        /// <summary>COM 포트 이름(예: COM3).</summary>
        public string PortName { get; set; } = "COM3";

        /// <summary>통신 속도(bps).</summary>
        public int BaudRate { get; set; } = 9600;

        public int DataBits { get; set; } = 8;

        /// <summary>
        /// <c>System.IO.Ports.Parity</c> 이름(None/Odd/Even/Mark/Space)을 문자열로 담는다.
        /// 대소문자는 구분하지 않는다.
        /// </summary>
        public string Parity { get; set; } = "None";

        /// <summary>
        /// <c>System.IO.Ports.StopBits</c> 이름(None/One/Two/OnePointFive)을 문자열로 담는다.
        /// 대소문자는 구분하지 않는다.
        /// </summary>
        public string StopBits { get; set; } = "One";
    }
}
