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
            _port.Write(buffer, offset, count);
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            _port.ReadTimeout = timeoutMilliseconds;
            return _port.Read(buffer, offset, count);
        }

        public void Dispose() => _port.Dispose();
    }
}
