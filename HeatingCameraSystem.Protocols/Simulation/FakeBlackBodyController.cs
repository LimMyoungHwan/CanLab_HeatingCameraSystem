using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 흑체 직접-제어 시뮬레이터. 타겟 설정 시 현재값이 타겟으로 즉시 스냅한다
    /// (FakePlcController의 기존 흑체 동작과 동일). 흑체 실제 직접-제어(시리얼/TCP) 스펙
    /// 확보 전까지 SimulationMode 및 실장비 모드 양쪽에서 대체로 사용된다.
    /// </summary>
    public sealed class FakeBlackBodyController : IBlackBodyController
    {
        private readonly ConcurrentDictionary<int, float> _pv = new();
        private readonly ConcurrentDictionary<int, float> _sv = new();
        private volatile bool _isConnected;

        public int Count { get; }
        public bool IsConnected => _isConnected;

        public FakeBlackBodyController(int count = 2) => Count = count;

        /// <summary>모든 흑체의 PV/SV를 25.0℃로 초기화하고 즉시 연결 상태가 된다. 실제 I/O는 없다.</summary>
        public Task ConnectAsync()
        {
            for (int i = 0; i < Count; i++)
            {
                _pv.TryAdd(i, 25.0f);
                _sv.TryAdd(i, 25.0f);
            }

            _isConnected = true;
            Log("ConnectAsync() -> OK (simulated, direct-control pending spec)");
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            _isConnected = false;
            Log("Disconnect() -> OK (simulated)");
        }

        /// <summary>SV 설정과 동시에 PV가 즉시 SV로 스냅한다. 승온·정착 시간 시뮬레이션은 없다.</summary>
        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
        {
            _sv[blackBodyIndex] = celsius;
            _pv[blackBodyIndex] = celsius; // 현재값이 타겟으로 즉시 스냅한다
            Log($"SetTemperatureAsync(BB{blackBodyIndex}, {celsius}) -> snaps");
            return Task.CompletedTask;
        }

        /// <summary>한 번도 설정하지 않은 인덱스는 25.0℃를 반환한다.</summary>
        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => Task.FromResult(_pv.GetOrAdd(blackBodyIndex, 25.0f));

        /// <summary>한 번도 설정하지 않은 인덱스는 25.0℃를 반환한다.</summary>
        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => Task.FromResult(_sv.GetOrAdd(blackBodyIndex, 25.0f));

        public void Dispose() => Disconnect();

        private static void Log(string msg) => Console.WriteLine($"[FakeBlackBody] {msg}");
    }
}
