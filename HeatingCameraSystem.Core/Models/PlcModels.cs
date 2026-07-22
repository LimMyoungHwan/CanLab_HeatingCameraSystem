using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>서보 축 식별자.</summary>
    public enum ServoAxis
    {
        X,
        Y
    }

    /// <summary>
    /// 원터치(수동) 제어 대상 장비. 각 항목은 PlcSettings 의 디바이스 주소에 매핑된다.
    /// (냉동기 M502~M504, 블로워 M505/M506, 칠러 P410, 도어락 P411, 조명 D280.0 등)
    /// </summary>
    public enum PlcEquipment
    {
        Cooler1st,
        Cooler2nd,
        CoolerRoom,
        Blower1,
        Blower2,
        Chiller,
        DoorLock,
        Lighting,
        PairGlass
    }

    /// <summary>
    /// 관리자 설정값 (읽기/쓰기 모두 지원). 온도류는 엔지니어링 단위(℃, %RH)로 표현하며
    /// PLC 스케일(×10 등) 변환은 PlcXgtClient 가 담당한다.
    /// </summary>
    public class PlcAdminSettings
    {
        public float OverheatLimit { get; set; }        // D4004 과열 상한
        public float CoolerRoomBoundary { get; set; }   // D1910 상온 냉동기 가동 경계
        public float Cooler2ndBoundary { get; set; }    // D1920 2차 냉동기 가동 경계
        public int CoolerDelayMinutes { get; set; }     // D78   1차→2차 냉동기 딜레이(분)
        public float BypassBoundary { get; set; }       // D1940 바이패스 동작 설정
        public float MfcMinOutput { get; set; }         // D1960 습도제어 MFC 최소출력
        public float MfcMaxOutput { get; set; }         // D1950 습도제어 MFC 최대출력
        public float PairGlassBoundary { get; set; }    // D1930 페어글라스 가동 경계
    }

    /// <summary>
    /// PLC 전체 상태 스냅샷. UI 폴링용 일괄 읽기 결과.
    /// 배열/리스트는 항상 non-null 로 초기화되어 UI 바인딩이 안전하다.
    /// </summary>
    public class PlcStatusSnapshot
    {
        // ── 챔버 온습도 ──
        public float CurrentTemperature { get; set; }
        public float TargetTemperature { get; set; }
        public float CurrentHumidity { get; set; }
        public float TargetHumidity { get; set; }

        // ── 흑체 ──
        public float BlackBody1Pv { get; set; }
        public float BlackBody1Sv { get; set; }
        public float BlackBody2Pv { get; set; }
        public float BlackBody2Sv { get; set; }

        // ── 서보/모션 ──
        public int ServoXPosition { get; set; }
        public int ServoYPosition { get; set; }
        public bool ServoXBusy { get; set; }
        public bool ServoYBusy { get; set; }
        public bool ServoXHomeComplete { get; set; }
        public bool ServoYHomeComplete { get; set; }
        public int ServoXErrorCode { get; set; }
        public int ServoYErrorCode { get; set; }
        public int CurrentPoint { get; set; }

        // ── 프로그램/기타 ──
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public float FanSpeedHz { get; set; }
        public float GasFlow { get; set; }

        // ── 장비 상태 (D60.x, D61.0) ──
        public bool Heater { get; set; }
        public bool Cooler1st { get; set; }
        public bool Cooler2nd { get; set; }
        public bool CoolerRoom { get; set; }
        public bool CoolerRoomBypass { get; set; }
        public bool DoorLamp { get; set; }
        public bool PairGlass { get; set; }
        public bool Mcf { get; set; }
        public bool Blower1 { get; set; }
        public bool Blower2 { get; set; }

        // ── 에러 (M4001~M4020) ──
        /// <summary>M4001~M4020 비트 상태 (인덱스 0 = M4001).</summary>
        public bool[] ErrorBits { get; set; } = new bool[PlcDeviceCatalog.ErrorNames.Length];

        // ── I/O 모니터 ──
        /// <summary>입력 P000~P01F (32비트).</summary>
        public bool[] InputBits { get; set; } = new bool[PlcDeviceCatalog.InputNames.Length];
        /// <summary>출력 P020~P03F (32비트).</summary>
        public bool[] OutputBits { get; set; } = new bool[PlcDeviceCatalog.OutputNames.Length];

        // ── 관리자 설정 (읽기 back) ──
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
