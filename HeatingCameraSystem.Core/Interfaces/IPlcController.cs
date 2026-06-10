using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface IPlcController
    {
        Task ConnectAsync(string ipAddress, int port = 502);
        void Disconnect();

        // 챔버 운전/정지
        Task StartChamberAsync();
        Task StopChamberAsync();

        // 온도 제어
        Task SetTargetTemperatureAsync(float temperature);
        Task<float> GetCurrentTemperatureAsync();

        // 습도 제어
        Task SetTargetHumidityAsync(float humidity);
        Task<float> GetCurrentHumidityAsync();

        // 서보 유닛 제어 (블랙바디 이동)
        Task MoveServoToPositionAsync(int positionIndex);
        Task<bool> IsServoAtPositionAsync();

        // 블랙바디 온도 제어
        Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature);
        Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex);

        // 비상 정지
        Task TriggerEmergencyStopAsync();
    }
}
