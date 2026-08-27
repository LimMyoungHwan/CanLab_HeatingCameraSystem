using System.Net;
using System.Net.Sockets;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// SR-800N 흑체와의 UDP 링크. UDP는 데이터그램 단위라 시리얼과 달리 프레임 재조립이 필요 없다
    /// — 수신 데이터그램 하나가 곧 응답 프레임 하나다.
    /// </summary>
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

        /// <summary>데이터그램 하나를 수신해 그대로 반환한다. ReadTimeoutMs 안에 응답이 없으면 SocketException.</summary>
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
