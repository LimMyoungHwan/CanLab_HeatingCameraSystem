using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// 카메라 한 대의 패널 뷰모델: 라이브 열영상 표시와 시리얼(RUN/셔터/NUC) 제어를 담당한다.
    /// 영상 출력은 시리얼과 의도적으로 분리되어 있다 — 시리얼이 null이면(포트 열기 실패 등)
    /// 시리얼 명령은 no-op이 되고 시리얼 패널만 숨겨질 뿐, 영상 스트리밍은 계속된다.
    /// </summary>
    public partial class CameraPanelViewModel : ObservableObject, IDisposable
    {
        private ICameraRuntime _runtime;
        private readonly Dispatcher _dispatcher;
        private readonly ICameraSerialClient? _serial;
        private readonly ThermalNucCorrector _nuc;
        private readonly CaptureStore _store;
        private readonly string _agentId;
        private readonly int _captureBurstCount;
        private readonly Func<CaptureResultMessage, Task>? _publishResult;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private string _status;

        [ObservableProperty]
        private string _serialNumber = "—";

        [ObservableProperty]
        private string _fpaTemperature = "—";

        [ObservableProperty]
        private string _serialStatus = string.Empty;

        [ObservableProperty]
        private string _captureStatus = string.Empty;

        /// <summary>시리얼 제어 가능 여부. 하트비트의 시리얼 건강 플래그(IsSerialConnected)가 이 값을 읽는다.</summary>
        public bool HasSerialControl => _serial is not null;

        public string AgentId => _agentId;

        public int CameraIndex => _runtime.CameraIndex;

        public CameraPanelViewModel(string title, string agentId, ICameraRuntime runtime, Dispatcher dispatcher, ThermalNucCorrector nuc, CaptureStore store, int captureBurstCount = 1, ICameraSerialClient? serial = null, Func<CaptureResultMessage, Task>? publishResult = null)
        {
            _title = title;
            _agentId = agentId;
            _runtime = runtime;
            _dispatcher = dispatcher;
            _nuc = nuc;
            _store = store;
            _captureBurstCount = captureBurstCount > 0 ? captureBurstCount : 1;
            _serial = serial;
            _publishResult = publishResult;
            _status = runtime.Status.ToString();

            _runtime.FrameReady += OnFrameReady;
            _runtime.StatusChanged += OnStatusChanged;
        }

        /// <summary>카메라 루프 스레드에서 프레임을 변환해 UI 스레드로 넘긴다. 변환 실패 프레임은 조용히 버린다.</summary>
        private void OnFrameReady(object? sender, ThermalFrame frame)
        {
            BitmapSource bmp;
            try
            {
                bmp = ThermalFrameBitmapSourceConverter.ToBitmapSource(_nuc.Apply(frame));
            }
            catch
            {
                return;
            }

            _dispatcher.InvokeAsync(() => LiveImage = bmp);
        }

        private void OnStatusChanged(object? sender, CameraRuntimeStatus status)
        {
            _dispatcher.InvokeAsync(() => Status = status.ToString());
        }

        // [S7] 패널을 재적재된 영상 런타임으로 향하게 하거나(Manager runtimeLoad), runtimeUnload 시
        // 해제한다(runtime = null). 영상 런타임만 교체된다 — 시리얼 클라이언트 + NUC는 그대로이므로
        // 카메라별 unload/reload가 COM 포트를 절대 흔들지 않는다. 필드 교체가 _runtime을 읽는 캡처
        // 명령들과 직렬화되도록 UI 스레드에서 호출할 것.
        public void RebindRuntime(ICameraRuntime? runtime)
        {
            _runtime.FrameReady -= OnFrameReady;
            _runtime.StatusChanged -= OnStatusChanged;

            if (runtime is null)
            {
                LiveImage = null;
                Status = CameraRuntimeStatus.Stopped.ToString();
                return;
            }

            _runtime = runtime;
            _runtime.FrameReady += OnFrameReady;
            _runtime.StatusChanged += OnStatusChanged;
            Status = _runtime.Status.ToString();
        }

        [RelayCommand]
        private async Task RestartAsync()
        {
            await _runtime.StopAsync();
            await _runtime.StartAsync();
        }

        /// <summary>버스트 수만큼 스냅샷을 NUC 보정 후 저장하고, 마지막 장을 캡처 결과로 Master에 발행한다.</summary>
        [RelayCommand]
        private async Task CaptureSaveAsync()
        {
            int saved = 0;
            ThermalFrame? lastFrame = null;
            CaptureRecord? lastRecord = null;
            try
            {
                for (int i = 0; i < _captureBurstCount; i++)
                {
                    bool forceFreshFrame = i > 0;
                    ThermalFrame? snap = await _runtime.CaptureSnapshotAsync(
                        maxAge: forceFreshFrame ? TimeSpan.Zero : TimeSpan.FromSeconds(1),
                        nextFrameTimeout: TimeSpan.FromSeconds(2));

                    if (snap is not null)
                    {
                        ThermalFrame corrected = _nuc.Apply(snap);
                        lastRecord = _store.Save(corrected, _agentId, _runtime.CameraIndex);
                        lastFrame = corrected;
                        saved++;
                    }
                }

                CaptureStatus = saved > 0
                    ? $"{saved}/{_captureBurstCount}장 저장됨 {DateTime.Now:HH:mm:ss}"
                    : "저장 실패: 프레임 없음";

                if (saved > 0 && lastFrame is not null && lastRecord is not null)
                    await PublishCaptureResultAsync(lastFrame, lastRecord);
            }
            catch (Exception ex)
            {
                CaptureStatus = $"저장 오류: {ex.Message}";
            }
        }

        private async Task PublishCaptureResultAsync(ThermalFrame frame, CaptureRecord record)
        {
            if (_publishResult is null) return;
            try
            {
                await _publishResult(new CaptureResultMessage
                {
                    AgentId = _agentId,
                    Alias = Title,
                    CameraIndex = _runtime.CameraIndex,
                    Source = CaptureSource.AgentUi,
                    CaptureId = Guid.NewGuid().ToString(),
                    IsSuccess = true,
                    ImagePath = record.Y16Path,
                    ImageBytes = ThermalPreviewEncoder.EncodeJpeg(frame),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraPanel] capture result publish failed: {ex.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task OpenShutterAsync() => RunSerialAsync(s => s.SetShutterAsync(true), "셔터 열림");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task CloseShutterAsync() => RunSerialAsync(s => s.SetShutterAsync(false), "셔터 닫힘");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task RunCameraAsync() => RunSerialAsync(s => s.SetCameraRunningAsync(true), "카메라 RUN");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task StopCameraAsync() => RunSerialAsync(s => s.SetCameraRunningAsync(false), "카메라 STOP");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task SaveConfigAsync() => RunSerialAsync(s => s.SaveConfigAsync(), "설정 저장");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private async Task RefreshInfoAsync()
        {
            if (_serial is null) return;

            try
            {
                SerialNumber = await _serial.ReadSerialNumberAsync();
                double fpa = await _serial.ReadFpaTemperatureAsync();
                FpaTemperature = $"{fpa:F1} ℃";
                SerialStatus = $"정보 갱신 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SerialStatus = $"읽기 실패: {ex.Message}";
            }
        }

        public async Task<double?> ReadCameraTemperatureAsync()
        {
            if (_serial is null) return null;
            try { return await _serial.ReadFpaTemperatureAsync().ConfigureAwait(false); }
            catch { return null; }
        }

        /// <summary>셔터를 닫아 평면필드를 캡처해 NUC 보정 테이블을 갱신한 뒤 셔터를 다시 연다.</summary>
        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private async Task RunNucAsync()
        {
            if (_serial is null) return;
            try
            {
                SerialStatus = "NUC: 셔터 닫고 평면필드 캡처…";
                await _serial.SetShutterAsync(false);
                await Task.Delay(400);

                ThermalFrame? flat = await AverageFramesAsync(12);
                await _serial.SetShutterAsync(true);

                if (flat is null)
                {
                    SerialStatus = "NUC 실패: 프레임 없음";
                    return;
                }

                _nuc.CaptureFromFlat(flat);
                SerialStatus = $"NUC 완료 (데드픽셀 {_nuc.DeadPixelCount}개) {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SerialStatus = $"NUC 오류: {ex.Message}";
                try { await _serial.SetShutterAsync(true); } catch { }
            }
        }

        private const double BiasTargetTolerance = 100;

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task RunBiasLowAsync(double? targetOverride) => RunAutoBiasAsync("LOW", targetOverride ?? 8500, 0x93);

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task RunBiasMidAsync(double? targetOverride) => RunAutoBiasAsync("MID", targetOverride ?? 5000, 0xA3);

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task RunBiasHighAsync(double? targetOverride) => RunAutoBiasAsync("HIGH", targetOverride ?? 5000, 0xD3);

        /// <summary>
        /// 목표 레벨 ±<see cref="BiasTargetTolerance"/> 안에 드는 바이어스 값을 탐색해 적용한다.
        /// 레시피 스텝이 목표를 지정하면 그 값이, 없으면 모드별 기본값이 들어온다.
        /// Cint는 카메라 특성이라 목표와 무관하게 모드별로 고정한다.
        /// </summary>
        private async Task RunAutoBiasAsync(string mode, double target, byte cint)
        {
            double targetMin = target - BiasTargetTolerance;
            double targetMax = target + BiasTargetTolerance;

            if (_serial is null) return;

            await _serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, 0x02);
            await _serial.SetBiasRegisterAsync(CameraBiasRegister.TintLsb, 0x73);
            await _serial.SetBiasRegisterAsync(CameraBiasRegister.Cint, cint);
            await _serial.SetBiasRegisterAsync(CameraBiasRegister.GskMsb, 0x01);
            await _serial.SetBiasRegisterAsync(CameraBiasRegister.Gfid, 0xAE);
            SerialStatus = $"BIAS: {target:F0} 탐색 중";
            (byte best, double bestError) = await FindBiasInRangeAsync(targetMin, targetMax, MeasureBiasAsync);
            await _serial.SetBiasAsync(best);
            SerialStatus = $"BIAS 완료: 0x{best:X2} (오차 {bestError:F0})";
        }

        private static async Task<(byte Value, double Error)> FindBiasInRangeAsync(
            double targetMin, double targetMax, Func<byte, Task<double>> measure)
        {
            var readings = new Dictionary<byte, double>();
            async Task<double> Read(byte value)
            {
                if (!readings.TryGetValue(value, out double result))
                {
                    result = await measure(value);
                    readings[value] = result;
                }
                return result;
            }

            double atZero = await Read(0);
            double atFull = await Read(byte.MaxValue);
            bool increasing = atFull >= atZero;
            byte best = 0;
            double bestError = double.MaxValue;
            void Consider(byte value, double level)
            {
                double error = level < targetMin ? targetMin - level : level > targetMax ? level - targetMax : 0;
                if (error < bestError) { best = value; bestError = error; }
            }

            for (int value = 0; value <= 0xF0; value += 0x10)
                Consider((byte)value, await Read((byte)value));
            int coarse = best & 0xF0;
            for (int value = coarse; value <= Math.Min(coarse + 0x0F, 0xFF); value++)
                Consider((byte)value, await Read((byte)value));

            int low = 0;
            int high = 0xFF;
            while (low <= high && bestError != 0)
            {
                byte value = (byte)((low + high) / 2);
                double level = await Read(value);
                Consider(value, level);
                if (level >= targetMin && level <= targetMax) break;
                bool tooLow = level < targetMin;
                if (tooLow == increasing) low = value + 1;
                else high = value - 1;
            }
            return (best, bestError);
        }

        internal static async Task<(byte Value, double Error)> FindBiasAsync(
            double target,
            Func<byte, Task<double>> measure)
        {
            byte best = 0;
            double bestError = double.MaxValue;
            double atZero = await measure(0);
            double atFull = await measure(byte.MaxValue);
            bool increasing = atFull >= atZero;
            int low = 0;
            int high = byte.MaxValue;

            while (low <= high)
            {
                byte value = (byte)((low + high) / 2);
                double level = await measure(value);
                double error = Math.Abs(level - target);
                if (error < bestError)
                {
                    best = value;
                    bestError = error;
                }
                if (error <= 100) break;

                bool tooLow = level < target;
                if (tooLow == increasing) low = value + 1;
                else high = value - 1;
            }

            return (best, bestError);
        }

        private async Task<double> MeasureBiasAsync(byte value)
        {
            await _serial!.SetBiasAsync(value);
            ThermalFrame frame = await _runtime.CaptureSnapshotAsync(
                maxAge: TimeSpan.Zero,
                nextFrameTimeout: TimeSpan.FromSeconds(2))
                ?? throw new InvalidOperationException("BIAS 측정 프레임이 없습니다.");
            var measurements = new double[5];
            for (int sample = 0; sample < measurements.Length; sample++)
            {
                if (sample > 0)
                {
                    frame = await _runtime.CaptureSnapshotAsync(
                        maxAge: TimeSpan.Zero,
                        nextFrameTimeout: TimeSpan.FromSeconds(2))
                        ?? throw new InvalidOperationException("BIAS measurement frame is missing.");
                }
                long sum = 0;
                foreach (ushort pixel in frame.Pixels) sum += pixel & 0x3FFF;
                measurements[sample] = (double)sum / frame.Pixels.Length;
                await Task.Delay(30);
            }
            Array.Sort(measurements);
            return measurements[2];
        }

        /// <summary>라이브 프레임을 frameCount장 누적 평균해(14비트 마스킹) NUC 평면필드용 프레임을 만든다.</summary>
        private async Task<ThermalFrame?> AverageFramesAsync(int frameCount)
        {
            ThermalFrame? first = _runtime.LatestFrame;
            if (first is null) return null;

            int len = first.Pixels.Length;
            var acc = new long[len];
            int used = 0;
            for (int k = 0; k < frameCount; k++)
            {
                ThermalFrame? f = _runtime.LatestFrame;
                if (f is not null && f.Pixels.Length == len)
                {
                    for (int i = 0; i < len; i++) acc[i] += f.Pixels[i] & 0x3FFF;
                    used++;
                }
                await Task.Delay(35);
            }

            if (used == 0) return null;

            var avg = new ushort[len];
            for (int i = 0; i < len; i++) avg[i] = (ushort)(acc[i] / used);
            return new ThermalFrame(avg, first.Width, first.Height, DateTimeOffset.Now);
        }

        /// <summary>영상 시작: 카메라 RUN 후 셔터 열기 (기본 셔터 닫힘 → 실 열영상 취득). 앱 시작 시 자동 호출.</summary>
        public Task StartLiveAsync() => RunSerialAsync(async s =>
        {
            await s.SetCameraRunningAsync(true);
            await s.SetShutterAsync(true);
        }, "영상 시작 (RUN+셔터 열림)");

        /// <summary>영상 종료: 셔터 닫기 후 카메라 STOP. 앱 종료 시 자동 호출.</summary>
        public Task StopLiveAsync() => RunSerialAsync(async s =>
        {
            await s.SetShutterAsync(false);
            await s.SetCameraRunningAsync(false);
        }, "영상 종료 (셔터 닫힘+STOP)");

        /// <summary>시리얼 명령 공통 실행기. _serial이 null이면 no-op — 영상 출력이 시리얼 성패에 좌우되지 않게 하는 지점이다.</summary>
        private async Task RunSerialAsync(Func<ICameraSerialClient, Task> action, string label)
        {
            if (_serial is null) return;
            try
            {
                await action(_serial);
                SerialStatus = $"{label} 적용됨";
            }
            catch (Exception ex)
            {
                SerialStatus = $"{label} 오류: {ex.Message}";
            }
        }

        public void Dispose()
        {
            _runtime.FrameReady -= OnFrameReady;
            _runtime.StatusChanged -= OnStatusChanged;
            _serial?.Dispose();
        }
    }
}
