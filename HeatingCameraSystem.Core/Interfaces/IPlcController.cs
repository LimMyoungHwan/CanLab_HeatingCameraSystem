using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// PLC 제어 추상화. 구현체: PlcXgtClient(LS XGT FEnet 전용 프로토콜), FakePlcController(시뮬).
    /// 온도/습도/흑체는 엔지니어링 단위(℃, %RH)를 주고받으며 PLC 스케일 변환은 구현체가 담당한다.
    /// </summary>
    public interface IPlcController
    {
        bool IsConnected { get; }
        Task ConnectAsync(string ipAddress, int port = 2004);
        void Disconnect();

        // ── 챔버 온도/습도 제어 ──
        Task StartChamberAsync();   // 온도제어 시작 (M10 + PC RUN)
        Task StopChamberAsync();    // 온도제어 정지 (M11)
        Task SetTargetTemperatureAsync(float temperature);
        Task<float> GetCurrentTemperatureAsync();
        Task SetTargetHumidityAsync(float humidity);
        Task<float> GetCurrentHumidityAsync();
        Task SetHumidityControlAsync(bool on);   // 습도제어 켜짐/꺼짐 (D281.0)

        // ── 흑체 온도 제어 (index 0=흑체1, 1=흑체2) ──
        Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature);
        Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex);

        // ── 서보/직교로봇 모션 ──
        Task MoveServoToPositionAsync(int positionIndex);        // 원터치 포인트 이동 (P601~)
        Task<bool> IsServoAtPositionAsync(int positionIndex);    // 현재포인트==idx && X/Y 비구동
        Task SetServoSpeedAsync(int percent);                    // 모터 전체 속도 1~100%
        Task JogAsync(ServoAxis axis, bool positive, bool on);   // JOG±: 누름(on=true)/뗌(on=false)
        Task HomeAsync(ServoAxis axis);                          // 원점 실행
        Task SetPointCoordinateAsync(int positionIndex, int x, int y);  // 포인트 목표좌표 쓰기
        Task<(int X, int Y)> GetPointCoordinateAsync(int positionIndex); // 포인트 목표좌표 읽기
        Task MoveToCoordinateAsync(int x, int y);                        // 절대좌표 직접 이동 (X/Y 쓰고 이동 트리거)

        // ── 수동 장비 제어 (원터치) ──
        Task SetEquipmentAsync(PlcEquipment equipment, bool on);
        Task SetFanSpeedAsync(float hz);   // 블로워 목표 회전수 (D350, 10.00~60.00Hz)

        // ── 관리자 설정 (일괄 쓰기) ──
        Task WriteAdminSettingsAsync(PlcAdminSettings settings);

        // ── 전체 상태/에러 일괄 읽기 (UI 폴링) ──
        Task<PlcStatusSnapshot> ReadStatusAsync();

        // ── 비상 정지 (PC 트리거) ──
        Task TriggerEmergencyStopAsync();
    }
}
