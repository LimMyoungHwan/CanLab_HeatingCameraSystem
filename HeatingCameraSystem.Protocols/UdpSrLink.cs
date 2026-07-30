using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    public sealed class UdpSrLink : ISrLink
    {
        private readonly BlackBodyUnitSettings _cfg;
        private readonly int _readTimeoutMs;
        private UdpClient? _client;

        public UdpSrLink(BlackBodyUnitSettings cfg, int readTimeoutMs)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _readTimeoutMs = readTimeoutMs;
        }

        public bool IsOpen => _client != null;

        public void Open()
        {
            if (IsOpen) return;
            _client = new UdpClient();
            _client.Client.ReceiveTimeout = _readTimeoutMs;
            _client.Connect(_cfg.IpAddress, _cfg.Port);
        }

        public void Close()
        {
            _client?.Close();
            _client = null;
        }

        public void Write(byte[] data) => _client!.Send(data, data.Length);

        public byte[] Read()
        {
            IPEndPoint endpoint = new(IPAddress.Any, 0);
            return _client!.Receive(ref endpoint);
        }

        public void DiscardInBuffer()
        {
            IPEndPoint endpoint = new(IPAddress.Any, 0);
            while (_client!.Available > 0) _client.Receive(ref endpoint);
        }

        public void Dispose() => Close();
    }
}
