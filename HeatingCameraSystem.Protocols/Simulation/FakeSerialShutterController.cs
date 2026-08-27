using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 셔터 컨트롤러. SimulationMode=true 시
    /// SerialShutterController(raw binary 시리얼 전송) 대신 사용한다.
    /// 포트 I/O 없이 카메라별 셔터 상태를 인메모리로만 유지하며 명령은 즉시 성공한다.
    /// 연결 전에 명령을 호출하면 InvalidOperationException을 던진다.
    /// </summary>
    public class FakeSerialShutterController : ISerialShutterController
    {
        private bool _isConnected;
        private readonly ConcurrentDictionary<int, bool> _shutterOpen = new();

        public bool IsConnected => _isConnected;

        public Task ConnectAsync()
        {
            _isConnected = true;
            Log("ConnectAsync() -> OK (simulated)");
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            _isConnected = false;
            Log("Disconnect() -> OK (simulated)");
        }

        public Task OpenShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _shutterOpen[cameraIndex] = true;
            Log($"OpenShutterAsync(cam={cameraIndex}) -> OPEN");
            return Task.CompletedTask;
        }

        public Task CloseShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _shutterOpen[cameraIndex] = false;
            Log($"CloseShutterAsync(cam={cameraIndex}) -> CLOSE");
            return Task.CompletedTask;
        }

        /// <summary>인메모리 상태를 반환한다. 한 번도 조작하지 않은 카메라는 닫힘(false)이 기본이다.</summary>
        public Task<bool> GetShutterStateAsync(int cameraIndex)
        {
            EnsureConnected();
            return Task.FromResult(_shutterOpen.GetOrAdd(cameraIndex, false));
        }

        public void Dispose() => Disconnect();

        private void EnsureConnected()
        {
            if (!_isConnected)
                throw new InvalidOperationException("FakeSerialShutterController is not connected. Call ConnectAsync first.");
        }

        private static void Log(string msg)
            => Console.WriteLine($"[FakeShutter] {msg}");
    }
}
