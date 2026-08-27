using System;
using System.IO.Ports;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// 카메라 셔터 시리얼 제어. ASCII 문자열이 아니라 raw binary 7바이트 고정 프레임을 전송한다
    /// (열기 04 00 01 00 00 00 00, 닫기 04 00 00 00 00 00 00). 하드웨어에 상태 조회 명령이
    /// 없으므로 셔터 상태는 마지막 명령 기준 소프트웨어 캐시로만 안다.
    /// </summary>
    public class SerialShutterController : ISerialShutterController
    {
        // 실제 카메라 셔터 바이트 프로토콜 (7바이트 고정)
        private static readonly byte[] _openBuffer  = { 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] _closeBuffer = { 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private readonly SerialSettings _s;
        private SerialPort? _port;
        private bool _isOpen; // 하드웨어 상태 조회 불가 → 소프트웨어 캐시

        public bool IsConnected => _port?.IsOpen ?? false;

        public SerialShutterController(SerialSettings? settings = null)
        {
            _s = settings ?? new SerialSettings();
        }

        /// <summary>포트를 연다. Parity/StopBits 문자열 파싱에 실패하면 None/One으로 조용히 대체한다.</summary>
        public Task ConnectAsync()
        {
            if (IsConnected) return Task.CompletedTask;

            var parity   = Enum.TryParse<Parity>(_s.Parity, true, out var p) ? p : Parity.None;
            var stopBits = Enum.TryParse<StopBits>(_s.StopBits, true, out var sb) ? sb : StopBits.One;

            _port = new SerialPort(_s.PortName, _s.BaudRate, parity, _s.DataBits, stopBits)
            {
                WriteTimeout = 2000
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

        /// <summary>셔터 열기 프레임을 전송한다. cameraIndex는 상위 레이어 식별자 전용 — 바이트 버퍼에는 미사용.</summary>
        public Task OpenShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _port!.Write(_openBuffer, 0, _openBuffer.Length);
            _isOpen = true;
            return Task.CompletedTask;
        }

        /// <summary>셔터 닫기 프레임을 전송한다. cameraIndex 취급은 <see cref="OpenShutterAsync"/>와 같다.</summary>
        public Task CloseShutterAsync(int cameraIndex)
        {
            EnsureConnected();
            _port!.Write(_closeBuffer, 0, _closeBuffer.Length);
            _isOpen = false;
            return Task.CompletedTask;
        }

        /// <summary>하드웨어 상태 조회 명령이 없으므로 마지막 명령 기준 소프트웨어 상태를 반환한다.</summary>
        public Task<bool> GetShutterStateAsync(int cameraIndex)
        {
            EnsureConnected();
            return Task.FromResult(_isOpen);
        }

        private void EnsureConnected()
        {
            if (!IsConnected) throw new InvalidOperationException("Serial port not connected.");
        }

        public void Dispose() => Disconnect();
    }
}
