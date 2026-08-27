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
        private readonly HardwareSettings _settings;
        private readonly TimeSpan _interval;
        private Timer? _timer;
        private int _running;

        private int _plcFails;
        private DateTime _plcNextAttemptUtc = DateTime.MinValue;

        public ConnectionMonitorService(
            IPlcController plc,
            HardwareSettings settings,
            TimeSpan? interval = null)
        {
            _plc = plc;
            _settings = settings;
            _interval = interval ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>점검 타이머를 시작한다. 콜백은 스레드풀에서 돌며 UI 스레드를 쓰지 않는다.</summary>
        public void Start() =>
            _timer = new Timer(async _ => await TickAsync(), null, _interval, _interval);

        public void Stop() => _timer?.Dispose();

        public void Dispose() => _timer?.Dispose();

        /// <summary>
        /// 점검 1회. Interlocked 가드로 재진입을 막아 이전 점검이 길어져도 겹치지 않는다.
        /// PLC가 끊겨 있으면 백오프 일정(<see cref="ComputeBackoff"/>)에 도달한 경우에만 재연결을
        /// 시도하고, 알람은 첫 실패와 복구 시에만 올려 반복 스팸을 막는다.
        /// </summary>
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
                        if (_plcFails > 0) AlarmSink.Raise(AlarmSeverity.Info, "PLC", "재연결 성공");
                        _plcFails = 0;
                        _plcNextAttemptUtc = DateTime.MinValue;
                        System.Diagnostics.Debug.WriteLine("[ConnMon] PLC reconnected.");
                    }
                    catch (Exception ex)
                    {
                        _plcFails++;
                        if (_plcFails == 1) AlarmSink.Raise(AlarmSeverity.Warning, "PLC", $"재연결 실패: {ex.Message}");
                        var wait = ComputeBackoff(_plcFails);
                        _plcNextAttemptUtc = now + wait;
                        System.Diagnostics.Debug.WriteLine(
                            $"[ConnMon] PLC reconnect failed ({_plcFails}x, next in {wait.TotalSeconds:0}s): {ex.Message}");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        /// <summary>연속 실패 횟수에 따른 지수 백오프 대기 시간을 구한다(점검 간격 × 2^(n-1), 상한 5분).</summary>
        private TimeSpan ComputeBackoff(int failures)
        {
            double seconds = _interval.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 10));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
        }
    }
}
