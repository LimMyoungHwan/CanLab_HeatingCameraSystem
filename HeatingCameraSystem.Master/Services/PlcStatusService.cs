using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public partial class PlcStatusService : ObservableObject
    {
        private readonly IPlcController? _plc;
        private readonly DispatcherTimer _timer;
        private bool[]? _prevErrorBits;
        private bool _wasConnected = true;
        private bool _polling;

        [ObservableProperty] private PlcStatusSnapshot _snapshot = new();
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isEmergencyStop;
        [ObservableProperty] private string _statusMessage = "PLC 대기";

        public event EventHandler<PlcStatusSnapshot>? Updated;

        public PlcStatusService(IPlcController? plc, int intervalSeconds = 1)
        {
            _plc = plc;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            _timer.Tick += async (_, _) => await PollAsync();
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        private async Task PollAsync()
        {
            if (_plc == null || _polling) return;
            _polling = true;
            try
            {
                var s = await _plc.ReadStatusAsync();
                Snapshot = s;
                IsEmergencyStop = s.ErrorBits.Length > 0 && s.ErrorBits[0];
                RaiseErrorEdges(s.ErrorBits);

                if (!_wasConnected) AlarmSink.Raise(AlarmSeverity.Info, "PLC", "연결 복구");
                _wasConnected = true;
                IsConnected = true;
                StatusMessage = $"갱신 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                if (_wasConnected) AlarmSink.Raise(AlarmSeverity.Error, "PLC", $"연결 끊김: {ex.Message}");
                _wasConnected = false;
                IsConnected = false;
                StatusMessage = $"읽기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlcStatus] poll failed: {ex.Message}");
            }
            finally
            {
                _polling = false;
                Updated?.Invoke(this, Snapshot);
            }
        }

        private void RaiseErrorEdges(bool[] bits)
        {
            var names = PlcDeviceCatalog.ErrorNames;
            for (int i = 0; i < bits.Length && i < names.Length; i++)
            {
                bool was = _prevErrorBits != null && i < _prevErrorBits.Length && _prevErrorBits[i];
                if (bits[i] && !was && !string.IsNullOrEmpty(names[i]))
                    AlarmSink.Raise(AlarmSeverity.Error, "PLC", names[i]);
            }
            _prevErrorBits = (bool[])bits.Clone();
        }
    }
}
