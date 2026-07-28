using System;
using System.IO.Ports;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>실제 RS-232 포트를 감싼 <see cref="ISrLink"/> 구현 (9600 8N1, 기기→Host EOM=CR+LF).</summary>
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
                NewLine = "\r\n",
                ReadTimeout = _readTimeoutMs,
                WriteTimeout = 2000
            };
            _port.Open();
        }

        public void Close()
        {
            if (_port?.IsOpen == true) _port.Close();
        }

        public void Write(string data) => _port!.Write(data);

        public string ReadLine() => _port!.ReadLine();

        public void DiscardInBuffer() => _port!.DiscardInBuffer();

        public void Dispose()
        {
            Close();
            _port?.Dispose();
            _port = null;
        }
    }
}
