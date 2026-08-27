using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>서보 축 식별자.</summary>
    public enum ServoAxis
    {
        /// <summary>X 축.</summary>
        X,

        /// <summary>Y 축.</summary>
        Y
    }

    /// <summary>
    /// 원터치(수동) 제어 대상 장비. 각 항목은 <see cref="Config.PlcSettings"/>의 디바이스 주소에 매핑된다.
    /// (냉동기 M502~M504, 블로워 M505/M506, 칠러 P410, 도어락 P411, 조명 D280.0 등)
    /// </summary>
    public enum PlcEquipment
    {
        /// <summary>1차 냉동기. <c>M502</c>.</summary>
        Cooler1st,

        /// <summary>2차 냉동기. <c>M503</c>.</summary>
        Cooler2nd,

        /// <summary>상온 냉동기. <c>M504</c>.</summary>
        CoolerRoom,

        /// <summary>블로워 1. <c>M505</c>.</summary>
        Blower1,

        /// <summary>블로워 2. <c>M506</c>.</summary>
        Blower2,

        /// <summary>칠러. <c>P410</c>.</summary>
        Chiller,

        /// <summary>도어락. <c>P411</c>.</summary>
        DoorLock,

        /// <summary>조명. <c>D280.0</c>.</summary>
        Lighting,

        /// <summary>페어글라스. <c>P370</c>.</summary>
        PairGlass
    }

    /// <summary>
    /// 관리자 설정값(읽기/쓰기 모두 지원). 온도류는 엔지니어링 단위(℃, %RH)로 표현하며
    /// PLC 스케일(×10 등) 변환은 Protocols의 PlcXgtClient가 담당한다.
    /// </summary>
    public class PlcAdminSettings
    {
        /// <summary>과열 상한(℃). <c>D4004</c>, ×10 스케일.</summary>
        public float OverheatLimit { get; set; }

        /// <summary>상온 냉동기 가동 경계(℃). <c>D1910</c>, ×10 스케일.</summary>
        public float CoolerRoomBoundary { get; set; }

        /// <summary>2차 냉동기 가동 경계(℃). <c>D1920</c>, ×10 스케일.</summary>
        public float Cooler2ndBoundary { get; set; }

        /// <summary>1차→2차 냉동기 딜레이(분). <c>D78</c>, raw.</summary>
        public int CoolerDelayMinutes { get; set; }

        /// <summary>바이패스 동작 설정. <c>D1940</c>, ×10 스케일.</summary>
        public float BypassBoundary { get; set; }

        /// <summary>습도제어 MFC 최소출력(%RH). <c>D1960</c>, ×10 스케일.</summary>
        public float MfcMinOutput { get; set; }

        /// <summary>습도제어 MFC 최대출력(%RH). <c>D1950</c>, ×10 스케일.</summary>
        public float MfcMaxOutput { get; set; }

        /// <summary>페어글라스 가동 경계(℃). <c>D1930</c>, ×10 스케일.</summary>
        public float PairGlassBoundary { get; set; }
    }

    /// <summary>
    /// PLC 전체 상태 스냅샷. UI 폴링용 일괄 읽기 결과.
    /// 배열/리스트는 항상 non-null 로 초기화되어 UI 바인딩이 안전하다.
    /// </summary>
    public class PlcStatusSnapshot
    {
        // ── 챔버 온습도 ──
        /// <summary>챔버 현재 온도(℃).</summary>
        public float CurrentTemperature { get; set; }

        /// <summary>챔버 목표 온도(℃).</summary>
        public float TargetTemperature { get; set; }

        /// <summary>챔버 현재 습도(%RH).</summary>
        public float CurrentHumidity { get; set; }

        /// <summary>챔버 목표 습도(%RH).</summary>
        public float TargetHumidity { get; set; }

        // ── 흑체 ──
        /// <summary>흑체 1 현재값(℃).</summary>
        public float BlackBody1Pv { get; set; }

        /// <summary>흑체 1 설정값(℃).</summary>
        public float BlackBody1Sv { get; set; }

        /// <summary>흑체 2 현재값(℃).</summary>
        public float BlackBody2Pv { get; set; }

        /// <summary>흑체 2 설정값(℃).</summary>
        public float BlackBody2Sv { get; set; }

        // ── 서보/모션 ──
        // PLC 워드는 0.1mm 단위 정수(x10 스케일). 여기서는 스케일 해제된 mm 값.
        /// <summary>X 축 현재 위치(mm). PLC 워드는 0.1mm 단위이며 여기서는 스케일 해제된 값.</summary>
        public float ServoXPosition { get; set; }

        /// <summary>Y 축 현재 위치(mm). PLC 워드는 0.1mm 단위이며 여기서는 스케일 해제된 값.</summary>
        public float ServoYPosition { get; set; }

        /// <summary>X 축 busy 비트.</summary>
        public bool ServoXBusy { get; set; }

        /// <summary>Y 축 busy 비트.</summary>
        public bool ServoYBusy { get; set; }

        /// <summary>X 축 원점 복귀 완료.</summary>
        public bool ServoXHomeComplete { get; set; }

        /// <summary>Y 축 원점 복귀 완료.</summary>
        public bool ServoYHomeComplete { get; set; }

        /// <summary>X 축 에러 코드.</summary>
        public int ServoXErrorCode { get; set; }

        /// <summary>Y 축 에러 코드.</summary>
        public int ServoYErrorCode { get; set; }

        /// <summary>현재 포인트 번호.</summary>
        public int CurrentPoint { get; set; }

        // ── 프로그램/기타 ──
        /// <summary>현재 스텝 번호.</summary>
        public int CurrentStep { get; set; }

        /// <summary>전체 스텝 수.</summary>
        public int TotalSteps { get; set; }

        /// <summary>팬 회전 속도(Hz).</summary>
        public float FanSpeedHz { get; set; }

        /// <summary>가스 유량.</summary>
        public float GasFlow { get; set; }

        // ── 장비 상태 (D60.x, D61.0) ──
        /// <summary>히터 상태.</summary>
        public bool Heater { get; set; }

        /// <summary>1차 냉동기 상태.</summary>
        public bool Cooler1st { get; set; }

        /// <summary>2차 냉동기 상태.</summary>
        public bool Cooler2nd { get; set; }

        /// <summary>상온 냉동기 상태.</summary>
        public bool CoolerRoom { get; set; }

        /// <summary>상온 냉동기 바이패스 상태.</summary>
        public bool CoolerRoomBypass { get; set; }

        /// <summary>도어 램프 상태.</summary>
        public bool DoorLamp { get; set; }

        /// <summary>페어글라스 상태.</summary>
        public bool PairGlass { get; set; }

        /// <summary>MFC 상태.</summary>
        public bool Mcf { get; set; }

        /// <summary>블로워 1 상태.</summary>
        public bool Blower1 { get; set; }

        /// <summary>블로워 2 상태.</summary>
        public bool Blower2 { get; set; }

        // ── 에러 (M4001~M4020) ──
        /// <summary>M4001~M4020 비트 상태(인덱스 0 = M4001).</summary>
        public bool[] ErrorBits { get; set; } = new bool[PlcDeviceCatalog.ErrorNames.Length];

        // ── I/O 모니터 ──
        /// <summary>입력 P000~P01F(32비트).</summary>
        public bool[] InputBits { get; set; } = new bool[PlcDeviceCatalog.InputNames.Length];

        /// <summary>출력 P020~P03F(32비트).</summary>
        public bool[] OutputBits { get; set; } = new bool[PlcDeviceCatalog.OutputNames.Length];

        // ── 관리자 설정(읽기 back) ──
        /// <summary>PLC에서 읽어온 관리자 설정값.</summary>
        public PlcAdminSettings Admin { get; set; } = new();
    }

    /// <summary>
    /// PLC 디바이스 이름표. 상태/에러/I-O 비트 인덱스에 대응하는 표시 라벨.
    /// 문서(A&amp;D PLC 제어 로직 설명서) 기준.
    /// </summary>
    public static class PlcDeviceCatalog
    {
        /// <summary>M4001~M4020 에러 라벨. 빈 문자열은 미사용 비트.</summary>
        public static readonly string[] ErrorNames =
        {
            "비상정지 스위치 동작",        // M4001
            "서보드라이브1 - X축 에러",     // M4002
            "서보드라이브2 - Y축 에러",     // M4003
            "과열방지 온도계 에러",         // M4004
            "메인콘트롤 온도 초과",         // M4005
            "인버터1 - FAN1 에러",          // M4006
            "인버터2 - FAN2 에러",          // M4007
            "SCR 전력조정기 에러",          // M4008
            "EOCR1 과부하",                 // M4009
            "EOCR2 과부하",                 // M4010
            "EOCR3 과부하",                 // M4011
            "EOCR4 과부하",                 // M4012
            "EOCR5 과부하",                 // M4013
            "칠러 EOCR 에러",               // M4014
            "",                             // M4015 (미사용)
            "도어 안전센서1 에러",          // M4016
            "도어 안전센서2 에러",          // M4017
            "도어 안전센서3 에러",          // M4018
            "",                             // M4019 (미사용)
            ""                              // M4020 (미사용)
        };

        /// <summary>입력 P000~P01F 라벨.</summary>
        public static readonly string[] InputNames =
        {
            "비상정지 신호",     // P000
            "EOCR 1 TRIP",       // P001
            "EOCR 2 TRIP",       // P002
            "EOCR 3 TRIP",       // P003
            "EOCR 4 TRIP",       // P004
            "EOCR 5 TRIP",       // P005
            "INV 1 ERR",         // P006
            "INV 2 ERR",         // P007
            "SERVO 1 ERR",       // P008
            "SERVO 2 ERR",       // P009
            "SCR ERR",           // P00A
            "MAIN TEMP ERR",     // P00B
            "OVER TEMP SIG",     // P00C
            "Chiller EOCR ERR",  // P00D
            "",                  // P00E
            "",                  // P00F
            "DOOR LOCK SEN 1",   // P010
            "DOOR LOCK SEN 2",   // P011
            "DOOR LOCK SEN 3",   // P012
            "", "", "", "", "", "", "", "", "", "", "", "", ""  // P013~P01F
        };

        /// <summary>출력 P020~P03F 라벨.</summary>
        public static readonly string[] OutputNames =
        {
            "", "",              // P020, P021
            "타워램프 녹",        // P022
            "타워램프 황",        // P023
            "타워램프 적",        // P024
            "부저",               // P025
            "", "",              // P026, P027
            "1차 냉동 마그네트",  // P028
            "2차 냉동 마그네트",  // P029
            "상온냉동 마그네트",  // P02A
            "히터 제어 마그네트", // P02B
            "상온냉동 바이패스",  // P02C
            "도어 LAMP",          // P02D
            "페어글라스",         // P02E
            "도어락 출력",        // P02F
            "SERVO ON 1",         // P030
            "SERVO ON 2",         // P031
            "", "",              // P032, P033
            "INV 1 RUN",          // P034
            "INV 2 RUN",          // P035
            "SERVO 1 RESET",      // P036
            "SERVO 2 RESET",      // P037
            "배기밸브",           // P038
            "칠러(서보냉각) 마그네트 ON", // P039
            "", "", "", "", "", ""  // P03A~P03F
        };
    }
}
