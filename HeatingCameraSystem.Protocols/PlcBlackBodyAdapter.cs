using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// 하위호환 어댑터: 흑체 온도를 PLC 경유로 제어하던 기존 경로를 <see cref="IBlackBodyController"/>로
    /// 감싼다. 흑체 직접-제어 컨트롤러가 주입되지 않은 경우(예: 기존 단위 테스트)의 폴백이며,
    /// 신규 프로덕션 경로는 직접-제어 구현(현재 Fake)을 주입한다. 서보 위치 이동은 여전히 PLC가 담당.
    /// </summary>
    public sealed class PlcBlackBodyAdapter : IBlackBodyController
    {
        private readonly IPlcController _plc;
        private readonly ConcurrentDictionary<int, float> _sv = new();

        public PlcBlackBodyAdapter(IPlcController plc)
            => _plc = plc ?? throw new ArgumentNullException(nameof(plc));

        public int Count => 2;
        public bool IsConnected => _plc.IsConnected;

        public Task ConnectAsync() => Task.CompletedTask; // PLC 연결은 AppServices가 별도 관리
        public void Disconnect() { }

        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
        {
            _sv[blackBodyIndex] = celsius;
            return _plc.SetBlackBodyTemperatureAsync(blackBodyIndex, celsius);
        }

        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => _plc.GetCurrentBlackBodyTemperatureAsync(blackBodyIndex);

        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => Task.FromResult(_sv.GetOrAdd(blackBodyIndex, 25.0f));

        public void Dispose() { }
    }
}
