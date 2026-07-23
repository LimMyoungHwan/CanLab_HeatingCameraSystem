using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    // One client per COM port. SerialPort is not concurrent → a single
    // SemaphoreSlim serializes every command (request write + 7-byte read).
    public class ClSerialCameraClient : ICameraSerialClient
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SerialPort? _port;

        public string PortName { get; }
        public bool IsOpen { get; private set; }

        public ClSerialCameraClient(string portName)
        {
            PortName = portName;
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            _port = new SerialPort(PortName, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 200,
                WriteTimeout = 200
            };
            _port.Open();
            IsOpen = true;
            return Task.CompletedTask;
        }

        public async Task<string> ReadSerialNumberAsync(CancellationToken ct = default)
        {
            byte a = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbA, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte b = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbB, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte c = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbC, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte d = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.SerialNbD, ClRw.Read, 0, ct).ConfigureAwait(false);
            return ClPacket.DecodeSerialNumber(a, b, c, d);
        }

        public async Task<double> ReadFpaTemperatureAsync(CancellationToken ct = default)
        {
            byte msb = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.FpaTempMsb, ClRw.Read, 0, ct).ConfigureAwait(false);
            byte lsb = await QueryAsync((byte)ClMainId.Detector, (byte)ClDetectorSubId.FpaTempLsb, ClRw.Read, 0, ct).ConfigureAwait(false);
            return ClPacket.DecodeFpaTemperature(msb, lsb);
        }

        public Task SetShutterAsync(bool open, CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.Shutter, ClRw.Write, open ? (byte)1 : (byte)0, ct);

        public Task SetCameraRunningAsync(bool running, CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.Camera, ClRw.Write, running ? (byte)1 : (byte)0, ct);

        public Task SaveConfigAsync(CancellationToken ct = default)
            => QueryAsync((byte)ClMainId.OperateCtrl, (byte)ClOperateCtrlSubId.SaveConfig, ClRw.Write, 1, ct);

        private async Task<byte> QueryAsync(byte mainId, byte subId, ClRw rw, byte data, CancellationToken ct)
        {
            EnsureOpen();
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                byte[] request = ClPacket.BuildRequest(mainId, subId, rw, data);
                _port!.Write(request, 0, 7);

                byte[] rx = new byte[7];
                int read = 0;
                while (read < 7)
                {
                    ct.ThrowIfCancellationRequested();
                    read += _port.Read(rx, read, 7 - read); // honors ReadTimeout
                }

                return ClPacket.ExtractPayload(rx);
            }
            finally
            {
                _gate.Release();
            }
        }

        private void EnsureOpen()
        {
            if (!IsOpen || _port is null || !_port.IsOpen)
            {
                throw new InvalidOperationException("Serial port not open. Call InitializeAsync first.");
            }
        }

        public void Dispose()
        {
            if (_port?.IsOpen == true)
            {
                _port.Close();
            }

            _port?.Dispose();
            _port = null;
            IsOpen = false;
            _gate.Dispose();
        }
    }
}
