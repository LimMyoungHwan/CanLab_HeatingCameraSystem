using System.IO.Ports;

namespace HeatingCameraSystem.Protocols.Cameras.CL
{
    // Keep the CL packet/timeout contract testable without opening a real COM port.
    internal interface IClSerialTransport : IDisposable
    {
        bool IsOpen { get; }
        void Open();
        void DiscardInBuffer();
        void Write(byte[] buffer, int offset, int count, int timeoutMilliseconds);
        int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds);
    }

    internal sealed class ClSerialTransport : IClSerialTransport
    {
        private readonly SerialPort _port;

        public ClSerialTransport(string portName)
        {
            _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
        }

        public bool IsOpen => _port.IsOpen;
        public void Open() => _port.Open();
        public void DiscardInBuffer() => _port.DiscardInBuffer();

        public void Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            _port.WriteTimeout = timeoutMilliseconds;
            try
            {
                _port.Write(buffer, offset, count);
            }
            catch (IOException ex)
            {
                // USB 링크가 wedge되면 드라이버가 ERROR_SEM_TIMEOUT(121)을 IOException으로 던진다.
                // 호출자들이 이미 처리하는 TimeoutException으로 바꿔 원인을 보존한 채 이 명령만 실패시킨다.
                throw new TimeoutException($"CL serial write failed: {ex.Message}", ex);
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            _port.ReadTimeout = timeoutMilliseconds;
            return _port.Read(buffer, offset, count);
        }

        public void Dispose() => _port.Dispose();
    }
}
