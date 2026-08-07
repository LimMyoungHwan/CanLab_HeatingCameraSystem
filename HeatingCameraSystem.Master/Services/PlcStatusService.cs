using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Localization;

namespace HeatingCameraSystem.Master.Services
{
    public partial class PlcStatusService : ObservableObject
    {
        private readonly IPlcController? _plc;
        private readonly IBlackBodyController? _blackBody;
        private readonly DispatcherTimer _timer;
        private bool[]? _prevErrorBits;
        private bool _wasConnected = true;
        private bool _polling;

        [ObservableProperty] private PlcStatusSnapshot _snapshot = new();
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isEmergencyStop;
        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Plc_StatusWaiting"];

        public event EventHandler<PlcStatusSnapshot>? Updated;

        public PlcStatusService(IPlcController? plc, IBlackBodyController? blackBody = null, int intervalSeconds = 1)
        {
            _plc = plc;
            _blackBody = blackBody;
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
                if (_blackBody != null)
                {
                    for (int i = 0; i < _blackBody.Count; i++)
                    {
                        float current = await _blackBody.GetCurrentTemperatureAsync(i);
                        float target = await _blackBody.GetTargetTemperatureAsync(i);
                        await _plc.WriteBlackBodyTemperaturesAsync(i, current, target);
                        if (i == 0) { s.BlackBody1Pv = current; s.BlackBody1Sv = target; }
                        if (i == 1) { s.BlackBody2Pv = current; s.BlackBody2Sv = target; }
                    }
                }
                Snapshot = s;
                IsEmergencyStop = s.ErrorBits.Length > 0 && s.ErrorBits[0];
                RaiseErrorEdges(s.ErrorBits);

                if (!_wasConnected) AlarmSink.Raise(AlarmSeverity.Info, "PLC", LocalizationManager.Instance["Plc_ConnRestored"]);
                _wasConnected = true;
                IsConnected = true;
                StatusMessage = string.Format(LocalizationManager.Instance["Dash_Refreshed"], DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                if (_wasConnected) AlarmSink.Raise(AlarmSeverity.Error, "PLC", string.Format(LocalizationManager.Instance["Plc_ConnLost"], ex.Message));
                _wasConnected = false;
                IsConnected = false;
                StatusMessage = string.Format(LocalizationManager.Instance["Dash_ReadFailed"], ex.Message);
                System.Diagnostics.Debug.WriteLine($"[PlcStatus] poll failed: {ex.Message}");
            }
            finally
            {
                _polling = false;
                Updated?.Invoke(this, Snapshot);
            }
        }

        public Task RefreshAsync() => PollAsync();

        private void RaiseErrorEdges(bool[] bits)
        {
            var names = PlcDeviceCatalog.ErrorNames;
            for (int i = 0; i < bits.Length && i < names.Length; i++)
            {
                bool was = _prevErrorBits != null && i < _prevErrorBits.Length && _prevErrorBits[i];
                if (bits[i] && !was && !string.IsNullOrEmpty(names[i]))
                    AlarmSink.Raise(AlarmSeverity.Error, "PLC", LocalizationManager.Instance.GetOrDefault("PlcErr_" + i, names[i]));
            }
            _prevErrorBits = (bool[])bits.Clone();
        }
    }
}
