namespace HeatingCameraSystem.Core.Config
{
    public class HardwareSettings
    {
        /// <summary>
        /// true이면 PLC / Serial Shutter를 가짜(Fake) 구현으로 대체한다.
        /// 실제 하드웨어가 없는 개발/시연/CI 환경에서 사용.
        /// 카메라(Agent 측)와 NATS는 별도. 카메라 시뮬은 agent.json 의 SimulationMode 로 켠다.
        /// </summary>
        public bool SimulationMode { get; set; } = false;

        /// <summary>
        /// BackgroundDataCleanupService 가 캡처 이미지/DB 이력을 보관할 일수.
        /// 기본 30일. 0 이면 정리 안 함(주의: 무한 증가).
        /// </summary>
        public int DataRetentionDays { get; set; } = 30;

        public PlcSettings Plc { get; set; } = new();
        public NatsSettings Nats { get; set; } = new();
        public SerialSettings Serial { get; set; } = new();
        public RecipeEngineSettings RecipeEngine { get; set; } = new();
    }

    public class PlcSettings
    {
        public string IpAddress { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 502;
        public int UnitId { get; set; } = 0;

        public int RegTempPv { get; set; } = 100;
        public int RegTempSv { get; set; } = 101;
        public int RegHumPv { get; set; } = 102;
        public int RegHumSv { get; set; } = 103;
        public int RegServoPosSv { get; set; } = 104;
        public int RegBb1TempSv { get; set; } = 105;
        public int RegBb2TempSv { get; set; } = 106;
        public int RegBb1TempPv { get; set; } = 107;
        public int RegBb2TempPv { get; set; } = 108;

        public int CoilRunStop { get; set; } = 10;
        public int CoilServoArrival { get; set; } = 11;
        public int CoilEStop { get; set; } = 12;
    }

    public class NatsSettings
    {
        public string Url { get; set; } = "nats://127.0.0.1:4222";
    }

    public class SerialSettings
    {
        public string PortName { get; set; } = "COM3";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "None";
        public string StopBits { get; set; } = "One";
    }

    public class RecipeEngineSettings
    {
        public float TemperatureTolerance { get; set; } = 0.5f;
        public int CaptureResultTimeoutSeconds { get; set; } = 30;
    }
}
