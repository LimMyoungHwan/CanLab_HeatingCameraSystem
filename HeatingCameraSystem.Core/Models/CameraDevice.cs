using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 카메라 장치 영구 레코드. LiteDB CameraDevice 컬렉션 + Manager state 공유.
    /// HardwareId = WMI PnPDeviceID (USB 포트 독립 영구 키).
    /// </summary>
    public class CameraDevice
    {
        public string   HardwareId      { get; set; } = string.Empty;   // PK
        public string   AgentId         { get; set; } = string.Empty;   // {PCId}_{HardwareIdHash8}
        public string   Alias           { get; set; } = string.Empty;   // 운영자 부여 이름
        public string   PCId            { get; set; } = string.Empty;   // 머신 식별자
        public int      OpenCvIndex     { get; set; }                   // VideoCapture 인덱스
        public CameraSerialSettings SerialSettings { get; set; } = new();
        public bool     IsApproved      { get; set; }
        public DateTime FirstSeen       { get; set; }
        public DateTime LastSeen        { get; set; }
    }
}
