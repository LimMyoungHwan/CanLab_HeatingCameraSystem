namespace HeatingCameraSystem.Core.Models
{
    public class CameraSerialSettings
    {
        public int    CameraIndex { get; set; }
        public string PortName    { get; set; } = "COM3";
        public int    BaudRate    { get; set; } = 9600;
        public int    DataBits    { get; set; } = 8;
        public string Parity      { get; set; } = "None";
        public string StopBits    { get; set; } = "One";
    }
}
