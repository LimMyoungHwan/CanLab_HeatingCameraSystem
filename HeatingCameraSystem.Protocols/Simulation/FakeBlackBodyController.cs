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

        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
        {
            _sv[blackBodyIndex] = celsius;
            _pv[blackBodyIndex] = celsius; // current snaps to target
            Log($"SetTemperatureAsync(BB{blackBodyIndex}, {celsius}) -> snaps");
            return Task.CompletedTask;
        }

        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => Task.FromResult(_pv.GetOrAdd(blackBodyIndex, 25.0f));

        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => Task.FromResult(_sv.GetOrAdd(blackBodyIndex, 25.0f));

        public void Dispose() => Disconnect();

        private static void Log(string msg) => Console.WriteLine($"[FakeBlackBody] {msg}");
    }
}
