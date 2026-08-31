namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    /// <summary>CL 시리얼 프로토콜 패킷의 메인 ID 바이트. 값은 하드웨어 계약이다.</summary>
    public enum ClMainId : byte
    {
        Detector    = 0x00,
        Nuc         = 0x10,
        UserConfig  = 0x20,
        OperateCtrl = 0x30,
        Debug       = 0xF0,
    }

    /// <summary>읽기/쓰기 구분 바이트.</summary>
    public enum ClRw : byte
    {
        Write = 0x00,
        Read  = 0x01,
    }

    /// <summary><see cref="ClMainId.Detector"/>의 하위 ID: S/N 레지스터 4바이트와 FPA 온도 MSB/LSB.</summary>
    public enum ClDetectorSubId : byte
    {
        SerialNbA  = 0x00,
        SerialNbB  = 0x01,
        SerialNbC  = 0x02,
        SerialNbD  = 0x03,
        Gfid       = 0x04,
        GskMsb     = 0x05,
        GskLsb     = 0x06,
        TintMsb    = 0x07,
        TintLsb    = 0x08,
        Cint       = 0x09,
        FpaTempMsb = 0x0A,
        FpaTempLsb = 0x0B,
    }

    /// <summary><see cref="ClMainId.OperateCtrl"/>의 하위 ID: 카메라·셔터 동작 제어와 설정 저장.</summary>
    public enum ClOperateCtrlSubId : byte
    {
        Camera     = 0x00,
        Shutter    = 0x01,
        SaveConfig = 0x02,
    }
}
