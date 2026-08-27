using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Localization;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// PLC 상태와 흑체 온도를 <b>서로 독립된</b> 타이머로 폴링한다. 흑체가 죽어도 PLC 스냅샷은
    /// 계속 갱신되고, PLC가 죽어도 흑체는 계속 읽힌다(대칭). 연결 상태·에러 메시지도 분리한다
    /// (<see cref="IsConnected"/>=PLC 전용, <see cref="IsBlackBodyConnected"/>=흑체 전용).
    /// 흑체 값의 <b>유일한</b> 판독자이며, 화면은 재판독 없이 여기서 나온 값을 소비한다.
    /// </summary>
    public partial class PlcStatusService : ObservableObject
    {
        private readonly IPlcController? _plc;
        private readonly IBlackBodyController? _blackBody;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _blackBodyTimer;
        private bool[]? _prevErrorBits;
        private bool _wasConnected = true;
        private bool _polling;
        private bool _blackBodyPolling;

        // 직접 판독한 흑체 값 캐시. null이면 아직 판독 전 → PLC 레지스터 값을 그대로 둔다.
        private float? _bb1Pv, _bb1Sv, _bb2Pv, _bb2Sv;

        [ObservableProperty] private PlcStatusSnapshot _snapshot = new();
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isEmergencyStop;
        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Plc_StatusWaiting"];

        // ── 흑체 전용 상태 (PLC 연결 상태와 절대 섞지 않는다) ──
        [ObservableProperty] private float _blackBody1Pv;
        [ObservableProperty] private float _blackBody1Sv;
        [ObservableProperty] private float _blackBody2Pv;
        [ObservableProperty] private float _blackBody2Sv;
        [ObservableProperty] private bool _blackBody1Faulted;
        [ObservableProperty] private bool _blackBody2Faulted;
        [ObservableProperty] private bool _isBlackBodyConnected;
        [ObservableProperty] private string _blackBodyStatusMessage = string.Empty;

        /// <summary>PLC 폴링이 끝날 때마다 발생한다. 판독에 실패해도 직전 스냅샷으로 발생한다.</summary>
        public event EventHandler<PlcStatusSnapshot>? Updated;

        /// <summary>흑체 판독이 끝날 때마다 발생. PLC 연결 여부와 무관하게 발생한다.</summary>
        public event EventHandler? BlackBodyUpdated;

        /// <summary>
        /// 타이머 두 개는 <see cref="DispatcherTimer"/>라서 폴링 콜백이 WPF UI 스레드에서 실행된다 —
        /// ObservableProperty를 마샬링 없이 바로 갱신해도 안전한 이유다.
        /// </summary>
        public PlcStatusService(IPlcController? plc, IBlackBodyController? blackBody = null, int intervalSeconds = 1)
        {
            _plc = plc;
            _blackBody = blackBody;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            _timer.Tick += async (_, _) => await PollAsync();
            _blackBodyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            _blackBodyTimer.Tick += async (_, _) => await PollBlackBodyAsync();
        }

        /// <summary>PLC 폴링을 시작한다. 흑체 컨트롤러가 있을 때만 흑체 타이머도 켠다.</summary>
        public void Start()
        {
            _timer.Start();
            if (_blackBody != null) _blackBodyTimer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _blackBodyTimer.Stop();
        }

        /// <summary>
        /// PLC 스냅샷 1회 폴링. <c>_polling</c> 플래그로 재진입을 막는다(판독이 주기보다 길어질 때 겹침 방지).
        /// 연결 상태가 바뀌는 에지에서만 알람을 올리고, 마지막에 항상 <see cref="Updated"/>를 발생시킨다.
        /// </summary>
        private async Task PollAsync()
        {
            if (_plc == null || _polling) return;
            _polling = true;
            try
            {
                var s = await _plc.ReadStatusAsync();
                MergeCachedBlackBody(s);
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

        /// <summary>
        /// 흑체 유닛별 독립 판독. 한 유닛이 실패해도 다른 유닛과 PLC 폴링에는 영향을 주지 않는다.
        /// PLC로의 표시값 미러링은 판독에 성공한 유닛에 대해서만 수행한다.
        /// </summary>
        private async Task PollBlackBodyAsync()
        {
            if (_blackBody == null || _blackBodyPolling) return;
            _blackBodyPolling = true;
            try
            {
                bool anyOk = false;
                string failure = string.Empty;

                for (int i = 0; i < _blackBody.Count; i++)
                {
                    try
                    {
                        float current = await _blackBody.GetCurrentTemperatureAsync(i);
                        float target = await _blackBody.GetTargetTemperatureAsync(i);
                        StoreBlackBody(i, current, target, faulted: false);
                        anyOk = true;

                        // PLC 미러링 실패는 흑체 판독 실패가 아니다 — 흑체 값은 이미 확보됐다.
                        if (_plc != null)
                        {
                            try { await _plc.WriteBlackBodyTemperaturesAsync(i, current, target); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlackBody{i}] PLC mirror failed: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex)
                    {
                        MarkBlackBodyFaulted(i);
                        failure = ex.Message;
                        System.Diagnostics.Debug.WriteLine($"[BlackBody{i}] poll failed: {ex.Message}");
                    }
                }

                IsBlackBodyConnected = anyOk;
                BlackBodyStatusMessage = failure.Length == 0
                    ? string.Format(LocalizationManager.Instance["Dash_Refreshed"], DateTime.Now.ToString("HH:mm:ss"))
                    : string.Format(LocalizationManager.Instance["Dash_ReadFailed"], failure);
            }
            finally
            {
                _blackBodyPolling = false;
                MergeCachedBlackBody(Snapshot);
                BlackBodyUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        private void StoreBlackBody(int index, float current, float target, bool faulted)
        {
            if (index == 0)
            {
                _bb1Pv = current; _bb1Sv = target;
                BlackBody1Pv = current; BlackBody1Sv = target; BlackBody1Faulted = faulted;
            }
            else if (index == 1)
            {
                _bb2Pv = current; _bb2Sv = target;
                BlackBody2Pv = current; BlackBody2Sv = target; BlackBody2Faulted = faulted;
            }
        }

        private void MarkBlackBodyFaulted(int index)
        {
            if (index == 0) BlackBody1Faulted = true;
            else if (index == 1) BlackBody2Faulted = true;
        }

        // 직접 판독값이 있으면 PLC 레지스터 값보다 우선한다(흑체가 1차 소스).
        private void MergeCachedBlackBody(PlcStatusSnapshot s)
        {
            if (_bb1Pv.HasValue) s.BlackBody1Pv = _bb1Pv.Value;
            if (_bb1Sv.HasValue) s.BlackBody1Sv = _bb1Sv.Value;
            if (_bb2Pv.HasValue) s.BlackBody2Pv = _bb2Pv.Value;
            if (_bb2Sv.HasValue) s.BlackBody2Sv = _bb2Sv.Value;
        }

        /// <summary>타이머를 기다리지 않고 PLC와 흑체를 즉시 1회씩 폴링한다.</summary>
        public async Task RefreshAsync()
        {
            await PollAsync();
            await PollBlackBodyAsync();
        }

        /// <summary>에러 비트의 상승 에지(0→1)에서만 알람을 올린다 — 에러가 유지되는 동안 반복 알람을 막는다.</summary>
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
