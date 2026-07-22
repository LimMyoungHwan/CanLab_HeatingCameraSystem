namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    public enum ClMainId : byte
    {
        Detector    = 0x00,
        Nuc         = 0x10,
        UserConfig  = 0x20,
        OperateCtrl = 0x30,
        Debug       = 0xF0,
    }

    public enum ClRw : byte
    {
        Write = 0x00,
        Read  = 0x01,
    }

    public enum ClDetectorSubId : byte
    {
        SerialNbA  = 0x00,
        SerialNbB  = 0x01,
        SerialNbC  = 0x02,
        SerialNbD  = 0x03,
        FpaTempMsb = 0x0A,
        FpaTempLsb = 0x0B,
    }

    public enum ClOperateCtrlSubId : byte
    {
        Camera     = 0x00,
        Shutter    = 0x01,
        SaveConfig = 0x02,
    }
}
