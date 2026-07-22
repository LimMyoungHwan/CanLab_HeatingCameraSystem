using HeatingCameraSystem.Core.Models;

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

        public List<CameraPairingEntry> CameraPairings { get; set; } = new();

        public PlcSettings Plc { get; set; } = new();
        public NatsSettings Nats { get; set; } = new();
        public SerialSettings Serial { get; set; } = new();
        public RecipeEngineSettings RecipeEngine { get; set; } = new();
    }

    /// <summary>
    /// LS(LSIS) XGT PLC CPU 계열. 디바이스 주소 문자열(D/M/P)을 전용 프로토콜의
    /// %-표기(%DW, %MX, %PX)로 변환할 때 규칙이 달라지므로 명시한다.
    /// 실제 설치 CPU 미확인 시 XGK 로 두고, 확인 후 hardware.json 에서 조정.
    /// </summary>
    public enum XgtCpuSeries
    {
        XGK,
        XGB,
        XGI
    }

    /// <summary>
    /// PLC(LS XGT + FEnet) 연결 및 디바이스 주소 매핑.
    /// 프로토콜: XGT 전용 프로토콜(TCP, 기본 포트 2004). Modbus 아님.
    /// 주소는 문서(A&amp;D PLC 제어 로직 설명서) 기준 논리 토큰("D100","M10","P000","D2520.0")으로 저장하고,
    /// PlcXgtClient 가 CpuSeries 에 맞춰 실제 %-주소로 변환한다.
    /// AGENTS.md 명시대로 실제 명세 확인 후 hardware.json 에서 조정 가능.
    /// </summary>
    public class PlcSettings
    {
        // ── 연결 ──
        public string IpAddress { get; set; } = "192.168.1.2";
        public int Port { get; set; } = 2004;                 // XGT FEnet 전용 프로토콜
        public int StationNo { get; set; } = 0;               // 국번
        public XgtCpuSeries CpuSeries { get; set; } = XgtCpuSeries.XGB;

        // P/M/L/K/F 비트 인덱스를 16진수로 통신할지 여부. 현재 하드웨어 XBC-DN64H(XGB) → true.
        // 비트가 엉뚱하게 읽/쓰이면 반전(XGK/XGI는 보통 false).
        public bool UseHexBitIndex { get; set; } = true;

        // ── 온도/습도 (word, ×10 스케일: 100.0℃ = 1000) ──
        public string TempPv { get; set; } = "D100";
        public string TempSv { get; set; } = "D102";
        public string HumPv { get; set; } = "D130";
        public string HumSv { get; set; } = "D131";

        // ── 흑체 (word, ×10) ──
        public string Bb1Pv { get; set; } = "D140";
        public string Bb1Sv { get; set; } = "D142";
        public string Bb2Pv { get; set; } = "D150";
        public string Bb2Sv { get; set; } = "D152";

        // ── 제어 비트 ──
        public string BitTempStart { get; set; } = "M10";     // 온도제어 시작
        public string BitTempStop { get; set; } = "M11";      // 온도제어 정지
        public string BitChamberRun { get; set; } = "M1910";  // PC RUN 표시등
        public string BitHumidityControl { get; set; } = "D281.0"; // 습도제어 켜짐
        // ponytail: 문서엔 챔버 비상정지 '상태'만 존재. PC 트리거 쓰기비트는 임의 지정 — 실제 비트 확인 후 교체.
        public string BitEmergencyStop { get; set; } = "M2000";

        // ── 서보/모션 ──
        public string ServoXPos { get; set; } = "D2540";
        public string ServoYPos { get; set; } = "D2640";
        public string ServoXBusyBit { get; set; } = "D2520.0";
        public string ServoYBusyBit { get; set; } = "D2620.0";
        public string ServoXHomeBit { get; set; } = "D2520.4";
        public string ServoYHomeBit { get; set; } = "D2620.4";
        public string ServoXErrorCode { get; set; } = "D2530";
        public string ServoYErrorCode { get; set; } = "D2630";
        public string ServoCurrentPoint { get; set; } = "D2740";
        // ponytail: '모터 전체 속도 1~100%'는 신규 제어 — 실제 대상 레지스터 미확정 placeholder.
        public string ServoSpeedPercent { get; set; } = "D2560";

        // 포인트 이동 원터치 비트: P601~P620 (번호 = base + (idx-1))
        public string ServoPointMoveBase { get; set; } = "P601";
        // 포인트 목표좌표: X=base+(idx-1)*stride, Y=X+2  (1P: D3010/D3012, 2P: D3020/D3022 ...)
        public string ServoPointXBase { get; set; } = "D3010";
        // 절대좌표 직접 이동(MoveToCoordinateAsync)의 Y 목표 워드. ponytail: 실제 주소 하드웨어 확인 후 교체.
        public string ServoPointYBase { get; set; } = "D3012";
        public int ServoPointStride { get; set; } = 10;
        public int ServoPointCount { get; set; } = 20;

        // 조그/원점 비트 (X: 문서값 / Y: 문서 미기재 → placeholder)
        public string BitJogXPlus { get; set; } = "P745";
        public string BitJogXMinus { get; set; } = "P746";
        public string BitHomeX { get; set; } = "P710";
        public string BitJogYPlus { get; set; } = "P725";   // ponytail: 문서 미기재 placeholder
        public string BitJogYMinus { get; set; } = "P726";  // ponytail: 문서 미기재 placeholder
        public string BitHomeY { get; set; } = "P720";

        // ── 수동 장비 원터치 비트 (PlcEquipment enum 매핑) ──
        public string EqCooler1st { get; set; } = "M502";
        public string EqCooler2nd { get; set; } = "M503";
        public string EqCoolerRoom { get; set; } = "M504";
        public string EqBlower1 { get; set; } = "M505";
        public string EqBlower2 { get; set; } = "M506";
        public string EqChiller { get; set; } = "P410";
        public string EqDoorLock { get; set; } = "P411";
        public string EqLighting { get; set; } = "D280.0";
        public string EqPairGlass { get; set; } = "P370";

        // ── 기타 상태/설정 워드 ──
        public string FanSpeed { get; set; } = "D350";   // ×100 (10.00~60.00Hz)
        public string GasFlow { get; set; } = "D1031";
        public string StepCurrent { get; set; } = "D3002";
        public string StepTotal { get; set; } = "D3000";

        // ── 장비 상태 램프 비트 (D60.x, D61.0) ──
        public string StatusHeater { get; set; } = "D60.1";
        public string StatusCooler1st { get; set; } = "D60.2";
        public string StatusCooler2nd { get; set; } = "D60.3";
        public string StatusCoolerRoom { get; set; } = "D60.4";
        public string StatusCoolerRoomBypass { get; set; } = "D60.5";
        public string StatusDoorLamp { get; set; } = "D60.6";
        public string StatusPairGlass { get; set; } = "D60.7";
        public string StatusMcf { get; set; } = "D60.8";
        public string StatusBlower1 { get; set; } = "D60.9";
        public string StatusBlower2 { get; set; } = "D61.0";

        // ── 에러/IO 블록 base ──
        public string ErrorBitBase { get; set; } = "M4001";  // M4001~M4020
        public string InputBitBase { get; set; } = "P000";   // P000~P01F (32)
        public string OutputBitBase { get; set; } = "P020";  // P020~P03F (32)

        // ── 관리자 설정 워드 ──
        public string AdminOverheatLimit { get; set; } = "D4004";      // ×10 ℃
        public string AdminCoolerRoomBoundary { get; set; } = "D1910"; // ×10 ℃
        public string AdminCooler2ndBoundary { get; set; } = "D1920";  // ×10 ℃
        public string AdminCoolerDelay { get; set; } = "D78";          // 분 (raw)
        public string AdminBypassBoundary { get; set; } = "D1940";     // ×10 ℃
        public string AdminMfcMinOutput { get; set; } = "D1960";       // ×10 %RH
        public string AdminMfcMaxOutput { get; set; } = "D1950";       // ×10 %RH
        public string AdminPairGlassBoundary { get; set; } = "D1930";  // ×10 ℃
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

        /// <summary>
        /// 온도 램프 스텝 간격(초). 현재온도→타겟온도를 도달시간에 걸쳐 선형으로 나눠 SV를 갱신한다.
        /// 기본 60초(예: 10→20℃, 도달시간 10분 → 분당 1℃).
        /// </summary>
        public int RampStepIntervalSeconds { get; set; } = 60;
    }
}
