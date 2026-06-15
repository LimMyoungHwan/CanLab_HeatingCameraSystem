using System;
using System.IO.Ports;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    public class SerialShutterController : ISerialShutterController
    {
        private readonly SerialSettings _s;
        private SerialPort? _port;

        public bool IsConnected => _port?.IsOpen ?? false;

        public SerialShutterController(SerialSettings? settings = null)
        {
            _s = settings ?? new SerialSettings();
        }

        public Task ConnectAsync()
        {
            if (IsConnected) return Task.CompletedTask;

            var parity = Enum.TryParse<Parity>(_s.Parity, true, out var p) ? p : Parity.None;
            var stopBits = Enum.TryParse<StopBits>(_s.StopBits, true, out var sb) ? sb : StopBits.One;

            _port = new SerialPort(_s.PortName, _s.BaudRate, parity, _s.DataBits, stopBits)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                NewLine = "\n"
            };
            _port.Open();
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            if (_port?.IsOpen == true) _port.Close();
            _port?.Dispose();
            _port = null;
        }

        public Task OpenShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _port!.WriteLine($"OPEN {cameraIndex}");
            return Task.CompletedTask;
        }

        public Task CloseShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _port!.WriteLine($"CLOSE {cameraIndex}");
            return Task.CompletedTask;
        }

        public Task<bool> GetShutterStateAsync(int cameraIndex)
        {
            EnsureConnected();
            _port!.WriteLine($"STATE {cameraIndex}");
            string response = _port.ReadLine().Trim();
            return Task.FromResult(response.Equals("OPEN", StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new InvalidOperationException("Serial port not connected.");
        }

        public void Dispose() => Disconnect();
    }
}
