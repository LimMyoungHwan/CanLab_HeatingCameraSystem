using System;
using System.IO.Ports;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    public sealed class SerialPortSrLink : ISrLink
    {
        private readonly SerialSettings _cfg;
        private readonly int _readTimeoutMs;
        private SerialPort? _port;

        public SerialPortSrLink(SerialSettings cfg, int readTimeoutMs)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _readTimeoutMs = readTimeoutMs;
        }

        public bool IsOpen => _port?.IsOpen ?? false;

        public void Open()
        {
            if (IsOpen) return;
            var parity = Enum.TryParse<Parity>(_cfg.Parity, true, out var p) ? p : Parity.None;
            var stop = Enum.TryParse<StopBits>(_cfg.StopBits, true, out var sb) ? sb : StopBits.One;
            _port = new SerialPort(_cfg.PortName, _cfg.BaudRate, parity, _cfg.DataBits, stop)
            {
                ReadTimeout = _readTimeoutMs,
                WriteTimeout = 2000
            };
            _port.Open();
        }

        public void Close()
        {
            if (_port?.IsOpen == true) _port.Close();
        }

        public void Write(byte[] data) => _port!.Write(data, 0, data.Length);

        public byte[] Read()
        {
            SerialPort port = _port ?? throw new InvalidOperationException("SR-800N serial port not open.");

            int sync;
            do { sync = port.ReadByte(); } while (sync != SrProtocol.Sync);

            int address = port.ReadByte();
            int sizeHi = port.ReadByte();
            int sizeLo = port.ReadByte();
            int size = (sizeHi << 8) | sizeLo;

            var frame = new byte[4 + size];
            frame[0] = (byte)sync;
            frame[1] = (byte)address;
            frame[2] = (byte)sizeHi;
            frame[3] = (byte)sizeLo;

            int read = 0;
            while (read < size)
            {
                int n = port.Read(frame, 4 + read, size - read);
                if (n <= 0) throw new TimeoutException("SR-800N serial read returned no data.");
                read += n;
            }

            return frame;
        }

        public void DiscardInBuffer() => _port!.DiscardInBuffer();

        public void Dispose()
        {
            Close();
            _port?.Dispose();
            _port = null;
        }
    }
}
