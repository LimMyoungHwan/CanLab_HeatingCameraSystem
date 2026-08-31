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
        /// <summary>PLC와의 세션이 살아있으면 true.</summary>
        bool IsConnected { get; }

        /// <summary>지정 IP/포트의 PLC에 접속한다. 기본 포트는 2004.</summary>
        Task ConnectAsync(string ipAddress, int port = 2004);

        /// <summary>PLC 연결을 끊는다.</summary>
        void Disconnect();

        // ── 챔버 온도/습도 제어 ──

        /// <summary>챔버 온도 제어를 시작한다(M10 + PC RUN).</summary>
        Task StartChamberAsync();

        /// <summary>챔버 온도 제어를 정지하고 램프를 끈다(M21, M11).</summary>
        Task StopChamberAsync();

        /// <summary>챔버 목표 온도(SV)를 설정한다.</summary>
        Task SetTargetTemperatureAsync(float temperature);

        /// <summary>챔버 제어 온도를 설정한다.</summary>
        Task SetControlTemperatureAsync(float temperature);

        /// <summary>챔버 현재 온도(PV)를 ℃로 읽는다.</summary>
        Task<float> GetCurrentTemperatureAsync();

        /// <summary>챔버 목표 습도를 %RH로 설정한다.</summary>
        Task SetTargetHumidityAsync(float humidity);

        /// <summary>챔버 현재 습도를 %RH로 읽는다.</summary>
        Task<float> GetCurrentHumidityAsync();

        /// <summary>습도 제어 ON/OFF 요청 시 D281에 트리거 값 1을 쓴다.</summary>
        Task SetHumidityControlAsync(bool on);

        // ── 흑체 온도 제어 (index 0=흑체1, 1=흑체2) ──

        /// <summary>지정 흑체의 목표 온도(SV)를 ℃로 설정한다.</summary>
        Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature);

        /// <summary>지정 흑체의 현재 온도(PV)를 ℃로 읽는다.</summary>
        Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex);

        /// <summary>지정 흑체의 현재/목표 온도를 한 번에 쓴다.</summary>
        Task WriteBlackBodyTemperaturesAsync(int blackBodyIndex, float currentTemperature, float targetTemperature);

        // ── 서보/직교로봇 모션 ──

        /// <summary>원터치 포인트로 이동한다(P601~).</summary>
        Task MoveServoToPositionAsync(int positionIndex);

        /// <summary>현재 포인트가 지정 인덱스이고 X/Y축이 구동 중이지 않으면 true.</summary>
        Task<bool> IsServoAtPositionAsync(int positionIndex);

        /// <summary>서보 모터 전체 속도를 1~100%로 설정한다.</summary>
        Task SetServoSpeedAsync(int percent);

        /// <summary>지정 축을 JOG 방향으로 누르거나 뗀다. on=true는 누름, on=false는 뗌.</summary>
        Task JogAsync(ServoAxis axis, bool positive, bool on);

        /// <summary>지정 축의 원점 복귀를 실행한다.</summary>
        Task HomeAsync(ServoAxis axis);

        /// <summary>원터치 포인트의 목표 좌표를 쓴다.</summary>
        Task SetPointCoordinateAsync(int positionIndex, float x, float y);

        /// <summary>원터치 포인트의 목표 좌표를 읽는다.</summary>
        Task<(float X, float Y)> GetPointCoordinateAsync(int positionIndex);

        /// <summary>절대 좌표로 직접 이동한다(X/Y 목표 좌표를 쓰고 이동 트리거).</summary>
        Task MoveToCoordinateAsync(float x, float y);

        // ── 수동 장비 제어 (원터치) ──

        /// <summary>지정 보조 장비(팬, 솔레노이드 등)의 ON/OFF를 설정한다.</summary>
        Task SetEquipmentAsync(PlcEquipment equipment, bool on);

        /// <summary>블로워 목표 회전수를 Hz로 설정한다(D350, 10.00~60.00Hz).</summary>
        Task SetFanSpeedAsync(float hz);

        // ── 관리자 설정 (일괄 쓰기) ──

        /// <summary>관리자 설정 값을 PLC에 일괄로 쓴다.</summary>
        Task WriteAdminSettingsAsync(PlcAdminSettings settings);

        // ── 전체 상태/에러 일괄 읽기 (UI 폴링) ──

        /// <summary>PLC의 전체 상태와 에러를 한 번에 읽어 UI 폴링에 쓴다.</summary>
        Task<PlcStatusSnapshot> ReadStatusAsync();

        // ── 비상 정지 (PC 트리거) ──

        /// <summary>PC에서 비상 정지를 트리거한다.</summary>
        Task TriggerEmergencyStopAsync();

        // ── 알람 처리 (모멘터리 트리거) ──

        /// <summary>챔버 모터 에러를 리셋한다(P525 모멘터리 트리거).</summary>
        Task ResetErrorAsync();

        /// <summary>부저를 끈다(P250).</summary>
        Task BuzzerOffAsync();
    }
}
