using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
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
