using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// PLC / Serial 연결 상태를 주기적으로 점검하고 끊긴 경우 자동 재연결.
    /// NATS는 NATS.Net 클라이언트가 내부적으로 자동 재연결하므로 별도 모니터링 안 함.
    /// </summary>
    public sealed class ConnectionMonitorService : IDisposable
    {
        private readonly IPlcController _plc;
        private readonly ISerialShutterController? _shutter;
        private readonly HardwareSettings _settings;
        private readonly TimeSpan _interval;
        private Timer? _timer;
        private int _running;

        public ConnectionMonitorService(
            IPlcController plc,
            ISerialShutterController? shutter,
            HardwareSettings settings,
            TimeSpan? interval = null)
        {
            _plc = plc;
            _shutter = shutter;
            _settings = settings;
            _interval = interval ?? TimeSpan.FromSeconds(30);
        }

        public void Start() =>
            _timer = new Timer(async _ => await TickAsync(), null, _interval, _interval);

        public void Stop() => _timer?.Dispose();

        public void Dispose() => _timer?.Dispose();

        private async Task TickAsync()
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
            try
            {
                if (!_plc.IsConnected)
                {
                    try
                    {
                        await _plc.ConnectAsync(_settings.Plc.IpAddress, _settings.Plc.Port);
                        System.Diagnostics.Debug.WriteLine("[ConnMon] PLC reconnected.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConnMon] PLC reconnect failed: {ex.Message}");
                    }
                }

                if (_shutter != null && !_shutter.IsConnected)
                {
                    try
                    {
                        await _shutter.ConnectAsync();
                        System.Diagnostics.Debug.WriteLine("[ConnMon] Serial reconnected.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConnMon] Serial reconnect failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
