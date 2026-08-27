using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 카메라 장치 영구 레코드. LiteDB CameraDevice 컬렉션 + Manager state 공유.
    /// HardwareId = WMI PnPDeviceID(USB 포트 독립 영구 키).
    /// </summary>
    public class CameraDevice
    {
        /// <summary>WMI PnPDeviceID. USB 포트 독립 영구 키(PK).</summary>
        public string   HardwareId      { get; set; } = string.Empty;

        /// <summary>AgentId. {PCId}_{HardwareIdHash8} 형식.</summary>
        public string   AgentId         { get; set; } = string.Empty;

        /// <summary>운영자가 부여한 이름.</summary>
        public string   Alias           { get; set; } = string.Empty;

        /// <summary>카메라 PC 머신 식별자.</summary>
        public string   PCId            { get; set; } = string.Empty;

        /// <summary>VideoCapture 인덱스.</summary>
        public int      OpenCvIndex     { get; set; }

        /// <summary>카메라 셔터용 시리얼 설정.</summary>
        public CameraSerialSettings SerialSettings { get; set; } = new();

        /// <summary>Manager 승인 여부.</summary>
        public bool     IsApproved      { get; set; }

        /// <summary>최초 발견 시각.</summary>
        public DateTime FirstSeen       { get; set; }

        /// <summary>마지막으로 본 시각.</summary>
        public DateTime LastSeen        { get; set; }
    }
}
