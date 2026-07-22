using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
    // Pure in-memory fake — no real serial I/O. Used in SimulationMode.
    public class FakeCameraSerialClient : ICameraSerialClient
    {
        public string PortName { get; }
        public bool IsOpen { get; private set; }
        public bool ShutterOpen { get; private set; }
        public bool CameraRunning { get; private set; }

        public FakeCameraSerialClient(string portName)
        {
            PortName = portName;
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        {
            string serial = PortName switch
            {
                "COM7" => "000100001",
                "COM8" => "000100002",
                _ => "000000000",
            };
            return Task.FromResult(serial);
        }

        public Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default)
        {
            // Deterministic per-port drift, always finite. ~30.0..32.9°C.
            int sum = 0;
            foreach (char ch in PortName)
            {
                sum += ch;
            }

            return Task.FromResult(30.0 + (sum % 30) / 10.0);
        }

        public Task SetShutterAsync(bool open, CancellationToken ct = default)
        {
            ShutterOpen = open;
            return Task.CompletedTask;
        }

        public Task SetCameraRunningAsync(bool running, CancellationToken ct = default)
        {
            CameraRunning = running;
            return Task.CompletedTask;
        }

        public void Dispose() => IsOpen = false;
    }
}
