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
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

        private readonly IPlcController _plc;
        private readonly ISerialShutterController? _shutter;
        private readonly HardwareSettings _settings;
        private readonly TimeSpan _interval;
        private Timer? _timer;
        private int _running;

        private int _plcFails;
        private DateTime _plcNextAttemptUtc = DateTime.MinValue;
        private int _shutterFails;
        private DateTime _shutterNextAttemptUtc = DateTime.MinValue;

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
                var now = DateTime.UtcNow;

                if (!_plc.IsConnected && now >= _plcNextAttemptUtc)
                {
                    try
                    {
                        await _plc.ConnectAsync(_settings.Plc.IpAddress, _settings.Plc.Port);
                        _plcFails = 0;
                        _plcNextAttemptUtc = DateTime.MinValue;
                        System.Diagnostics.Debug.WriteLine("[ConnMon] PLC reconnected.");
                    }
                    catch (Exception ex)
                    {
                        _plcFails++;
                        var wait = ComputeBackoff(_plcFails);
                        _plcNextAttemptUtc = now + wait;
                        System.Diagnostics.Debug.WriteLine(
                            $"[ConnMon] PLC reconnect failed ({_plcFails}x, next in {wait.TotalSeconds:0}s): {ex.Message}");
                    }
                }

                if (_shutter != null && !_shutter.IsConnected && now >= _shutterNextAttemptUtc)
                {
                    try
                    {
                        await _shutter.ConnectAsync();
                        _shutterFails = 0;
                        _shutterNextAttemptUtc = DateTime.MinValue;
                        System.Diagnostics.Debug.WriteLine("[ConnMon] Serial reconnected.");
                    }
                    catch (Exception ex)
                    {
                        _shutterFails++;
                        var wait = ComputeBackoff(_shutterFails);
                        _shutterNextAttemptUtc = now + wait;
                        System.Diagnostics.Debug.WriteLine(
                            $"[ConnMon] Serial reconnect failed ({_shutterFails}x, next in {wait.TotalSeconds:0}s): {ex.Message}");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private TimeSpan ComputeBackoff(int failures)
        {
            double seconds = _interval.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 10));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
        }
    }
}
