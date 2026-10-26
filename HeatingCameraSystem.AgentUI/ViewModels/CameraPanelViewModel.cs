using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Localization;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// 카메라 한 대의 패널 뷰모델: 라이브 열영상 표시와 시리얼(RUN/셔터/NUC) 제어를 담당한다.
    /// 시리얼이 null이면(포트 열기 실패 등) 시리얼 패널을 숨기고 영상 스트리밍은 계속한다.
    /// 보정이 필수인 원격 BIAS 명령은 시리얼 연결이 없으면 명시적으로 실패한다.
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
        private readonly CancellationTokenSource? _infoPollCts;
        private readonly SemaphoreSlim _biasGate = new(1, 1);
        private readonly CancellationTokenSource _biasLifetimeCts = new();
        private string? _lastTemperatureFailure;
        private AppliedBiasResult? _lastAppliedBias;
        private bool _disposed;

        private const string Unknown = "—";
        private static readonly TimeSpan InfoPollInterval = TimeSpan.FromSeconds(1);

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private string _status;

        [ObservableProperty]
        private string _serialNumber = Unknown;

        [ObservableProperty]
        private string _fpaTemperature = Unknown;

        [ObservableProperty]
        private string _biasLsb = Unknown;

        [ObservableProperty]
        private string _serialStatus = string.Empty;

        [ObservableProperty]
        private string _captureStatus = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEditBias))]
        [NotifyCanExecuteChangedFor(nameof(RunBiasLowCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunBiasMidCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunBiasHighCommand))]
        [NotifyCanExecuteChangedFor(nameof(RestoreManualBiasCommand))]
        private bool _isBiasBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RestoreManualBiasCommand))]
        private string _manualBiasOperation = CameraControlOps.BiasLow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManualBiasValuePreview))]
        [NotifyCanExecuteChangedFor(nameof(RestoreManualBiasCommand))]
        private string _manualBiasValueText = string.Empty;

        public bool CanEditBias => HasSerialControl && !IsBiasBusy && !_disposed;

        public string ManualBiasValuePreview => TryParseManualBiasValue(ManualBiasValueText, out byte value)
            ? $"0x{value:X2} ({value})" : string.Empty;

        private bool CanRestoreManualBias => CanEditBias
            && BiasExecutionPolicy.IsBiasOperation(ManualBiasOperation)
            && TryParseManualBiasValue(ManualBiasValueText, out _);

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

            if (_serial is not null)
            {
                _infoPollCts = new CancellationTokenSource();
                ICameraSerialClient pollSerial = _serial;
                CancellationToken pollToken = _infoPollCts.Token;
                _ = Task.Run(() => PollInfoLoopAsync(pollSerial, pollToken));
            }
        }

        /// <summary>카메라 루프 스레드에서 프레임을 변환해 UI 스레드로 넘긴다. 변환 실패 프레임은 조용히 버린다.</summary>
        private void OnFrameReady(object? sender, ThermalFrame frame)
        {
            BitmapSource bmp;
            try
            {
                bmp = ThermalFrameBitmapSourceConverter.ToBitmapSource(frame);
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
                string serialNumber = await _serial.ReadSerialNumberAsync();
                double fpa = await _serial.ReadFpaTemperatureAsync();
                byte lsb = await _serial.ReadBiasAsync();
                ApplyInfo((serialNumber, fpa, lsb));
                await ReadOutputFormatAsync();
                SerialStatus = $"정보 갱신 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SerialStatus = $"읽기 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// S/N·FPA·LSB 표시를 1초마다 갱신한다. 시리얼 I/O는 <see cref="ICameraSerialClient"/> 구현이
        /// SerialPort를 동기 호출하므로 반드시 UI 스레드 밖에서 돌려야 한다 — 그래서 DispatcherTimer가
        /// 아니라 백그라운드 루프이고, 표시 반영만 디스패처로 넘긴다.
        /// <para>
        /// S/N도 매 틱 읽는다(왕복 7회). 불변값이 아니라 모드 신호이기 때문이다 — 카메라가 STOP이면
        /// 전부 0인 S/N이 나오므로(<c>CameraComPairingStartRecoveryTests</c>가 이 동작을 고정한다)
        /// 운영 중 S/N이 바뀌면 카메라 모드가 바뀐 것으로 읽는다. 1회만 읽으면 그 전환이 안 보인다.
        /// </para>
        /// <para>
        /// 주기는 <see cref="PeriodicTimer"/>가 아니라 사이클 끝의 지연으로 보장한다. PeriodicTimer는
        /// 놓친 틱을 즉시 돌려주므로, 카메라가 응답을 멈춰 7회 × ReadTimeout(200ms)이 걸리면 폴링이
        /// 쉬는 구간 없이 연속으로 돌아 셔터·캡처와 공유하는 포트 락을 굶긴다.
        /// </para>
        /// </summary>
        private async Task PollInfoLoopAsync(ICameraSerialClient serial, CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(InfoPollInterval, ct).ConfigureAwait(false);

                    (string SerialNumber, double Fpa, byte Lsb)? reading = null;
                    try
                    {
                        string serialNumber = await serial.ReadSerialNumberAsync(ct).ConfigureAwait(false);
                        double fpa = await serial.ReadFpaTemperatureAsync(ct).ConfigureAwait(false);
                        byte lsb = await serial.ReadBiasAsync(ct).ConfigureAwait(false);
                        reading = (serialNumber, fpa, lsb);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // 링크 끊김·타임아웃은 화면만 비우고 다음 틱에 복구한다. 사유 로그는 하트비트
                        // 경로(NoteTemperatureFailure)가 이미 남기므로 여기서 또 남기면 1초마다 중복된다.
                    }

                    _ = _dispatcher.InvokeAsync(() => ApplyInfo(reading));
                }
            }
            catch (OperationCanceledException)
            {
                // 패널 Dispose — 정상 종료 경로.
            }
        }

        /// <summary>
        /// 읽은 값을 표시 속성에 반영한다. <paramref name="reading"/>이 null이면(읽기 실패) 세 값을 모두
        /// 비운다 — 셋 다 실시간 값이라, 못 읽은 상태에서 옛값을 남기면 유효한 현재값처럼 보인다.
        /// </summary>
        internal void ApplyInfo((string SerialNumber, double Fpa, byte Lsb)? reading)
        {
            if (reading is not { } value)
            {
                SerialNumber = Unknown;
                FpaTemperature = Unknown;
                BiasLsb = Unknown;
                return;
            }

            SerialNumber = value.SerialNumber;
            FpaTemperature = $"{value.Fpa:F1} ℃";
            BiasLsb = $"0x{value.Lsb:X2}";
        }

        public CameraOutputFormat[] OutputFormats { get; } = Enum.GetValues<CameraOutputFormat>();

        [ObservableProperty]
        private CameraOutputFormat _selectedOutputFormat = CameraOutputFormat.Y16;

        [ObservableProperty]
        private string _outputFormatText = "—";

        /// <summary>콤보박스 선택값도 실제 값으로 맞춰, 아무것도 안 바꾸고 누른 '적용'이 포맷을 뒤집지 않게 한다.</summary>
        public async Task ReadOutputFormatAsync()
        {
            if (_serial is null) return;

            CameraOutputFormat format = await _serial.ReadOutputFormatAsync();
            SelectedOutputFormat = format;
            OutputFormatText = DescribeOutputFormat(format);
        }

        internal static string DescribeOutputFormat(CameraOutputFormat format) => format switch
        {
            CameraOutputFormat.Y16 => "Y16 (16비트 원본 · .raw 저장 가능)",
            CameraOutputFormat.Uyvy => "UYVY (8비트 · .raw 저장 불가)",
            _ => $"알 수 없음 (0x{(byte)format:X2})"
        };

        /// <summary>
        /// 선택한 출력 포맷을 카메라에 쓴다. 쓰는 즉시 카메라가 USB 링크를 끊으므로
        /// (<c>참고/util/Viewer.cpp:414-425</c>) 이어지는 명령은 실패하는 게 정상이다 —
        /// 여기서 예외를 실패로 보고하지 않고 재시작 안내로 바꾸는 이유다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private async Task ApplyOutputFormatAsync()
        {
            if (_serial is null) return;

            CameraOutputFormat target = SelectedOutputFormat;
            try
            {
                await _serial.SetOutputFormatAsync(target);
            }
            catch (Exception ex)
            {
                SerialStatus = $"출력 포맷 변경 실패: {ex.Message}";
                return;
            }

            OutputFormatText = $"{DescribeOutputFormat(target)} — 적용 대기";
            SerialStatus = $"출력 포맷을 {target}로 변경했습니다. 카메라가 링크를 끊었으니 "
                           + "AgentUI를 재시작한 뒤 '정보 읽기'로 확인하고, '설정 저장'으로 카메라에 영구 저장하세요.";
        }

        /// <summary>
        /// 하트비트와 캡처가 Master로 실어 보낼 카메라(FPA) 온도를 읽는다. 실패는 null로 흡수하되
        /// 사유를 로그에 남긴다 — 이 값이 null이면 Master 레시피 결과의 "카메라 온도"가 조용히
        /// 비어 버려서, 로그가 없으면 현장에서 원인을 찾을 방법이 없다.
        /// </summary>
        public async Task<double?> ReadCameraTemperatureAsync()
        {
            if (_serial is null)
            {
                NoteTemperatureFailure("시리얼 연결 없음(영상 전용으로 등록되었거나 COM 포트 열기 실패)");
                return null;
            }

            try
            {
                double value = await _serial.ReadFpaTemperatureAsync().ConfigureAwait(false);
                NoteTemperatureRecovered(value);
                return value;
            }
            catch (Exception ex)
            {
                NoteTemperatureFailure($"시리얼 읽기 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 생산 저장 규칙의 <c>.raw</c> 픽셀(0,0)에 심을 FPA 레지스터 원시값.
        /// 실패는 null로 흡수한다 — FPA를 못 읽었다고 촬영 자체를 버릴 이유는 없다.
        /// </summary>
        public async Task<short?> ReadFpaTemperatureRawAsync()
        {
            if (_serial is null) return null;

            try
            {
                return await _serial.ReadFpaTemperatureRawAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AgentUiLog.Logger.Warning("[{AgentId}] FPA raw 읽기 실패: {Reason}", _agentId, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 같은 사유는 한 번만 남긴다. 하트비트가 5초마다 이 경로를 타므로 매번 남기면
        /// 로그가 같은 줄로 가득 차 정작 다른 단서가 묻힌다.
        /// </summary>
        private void NoteTemperatureFailure(string reason)
        {
            if (_lastTemperatureFailure == reason) return;

            _lastTemperatureFailure = reason;
            AgentUiLog.Logger.Warning(
                "[{AgentId}] 카메라(FPA) 온도를 읽지 못했습니다 — {Reason}. Master 레시피 결과의 카메라 온도가 빈 값이 됩니다.",
                _agentId, reason);
        }

        private void NoteTemperatureRecovered(double value)
        {
            if (_lastTemperatureFailure is null) return;

            _lastTemperatureFailure = null;
            AgentUiLog.Logger.Information("[{AgentId}] 카메라(FPA) 온도 읽기가 복구되었습니다: {Temperature:F1}℃", _agentId, value);
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

        // 탐색 대상은 GSK_LSB 하나뿐이고 나머지 레지스터는 고정값이다(참고/AISEN_CODE/main.py:1531-1574).
        private const byte TintMsbValue = 0x02;
        private const byte TintLsbValue = 0x73;
        private const byte GskMsbValue = 0x01;
        private const byte GfidValue = 0xAE;

        /// <summary>
        /// 현재 적용한 BIAS 결과. 자동 탐색과 수동 복원을 구분하며 같은 값의 재적용도 새 ID를
        /// 발급한다. 적용 시작·실패·취소 시 이전 결과를 무효화한다.
        /// </summary>
        public AppliedBiasResult? LastAppliedBias => _lastAppliedBias;

        /// <summary>기존 저장 경로용 호환 getter. 결과의 출처·세대는 LastAppliedBias를 확인한다.</summary>
        public string? LastBiasJson => LastAppliedBias?.Json;

        /// <summary>기존 명령 처리기용 호환 getter. 현재 유효한 적용 결과가 있을 때만 성공이다.</summary>
        public bool LastBiasSucceeded => LastAppliedBias is not null;

        [RelayCommand(CanExecute = nameof(CanEditBias))]
        private Task RunBiasLowAsync(double? targetOverride) => RunManualBiasAsync(CameraControlOps.BiasLow, targetOverride);

        [RelayCommand(CanExecute = nameof(CanEditBias))]
        private Task RunBiasMidAsync(double? targetOverride) => RunManualBiasAsync(CameraControlOps.BiasMid, targetOverride);

        [RelayCommand(CanExecute = nameof(CanEditBias))]
        private Task RunBiasHighAsync(double? targetOverride) => RunManualBiasAsync(CameraControlOps.BiasHigh, targetOverride);

        /// <summary>
        /// 원격 레시피에서 호출하는 한 번의 보정 시도. 예외와 취소를 호출자에게 전달하며,
        /// 재시도와 bias.json 저장은 Agent의 명령 처리기가 담당한다.
        /// </summary>
        public Task RunBiasOperationAsync(string operation, double? targetOverride, CancellationToken cancellationToken)
            => operation switch
            {
                CameraControlOps.BiasLow => RunAutoBiasAsync(operation, "LOW", targetOverride ?? CameraBiasDefaults.Low, 0x93, cancellationToken),
                CameraControlOps.BiasMid => RunAutoBiasAsync(operation, "MID", targetOverride ?? CameraBiasDefaults.Mid, 0xA3, cancellationToken),
                CameraControlOps.BiasHigh => RunAutoBiasAsync(operation, "HIGH", targetOverride ?? CameraBiasDefaults.High, 0xD3, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "지원하지 않는 BIAS 명령입니다.")
            };

        private async Task RunManualBiasAsync(string operation, double? targetOverride)
        {
            try
            {
                await RunBiasOperationAsync(operation, targetOverride, CancellationToken.None);
            }
            catch (Exception)
            {
                // 수동 버튼은 아래 상태·로그로 실패를 표시한다. 원격 실행은 위 메서드를 직접
                // 호출하므로 예외가 ACK 처리까지 전파된다.
            }
        }

        internal static bool TryParseManualBiasValue(string? text, out byte value)
        {
            value = 0;
            string hex = text?.Trim() ?? string.Empty;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            return hex.Length is >= 1 and <= 2
                && byte.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static (string Mode, byte Cint) BiasMode(string operation) => operation switch
        {
            CameraControlOps.BiasLow => ("LOW", 0x93),
            CameraControlOps.BiasMid => ("MID", 0xA3),
            CameraControlOps.BiasHigh => ("HIGH", 0xD3),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "지원하지 않는 BIAS 대역입니다.")
        };

        private static string BiasText(string key, params object[] arguments)
            => string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance[key], arguments);

        private void SetAppliedBias(AppliedBiasResult? result)
        {
            _lastAppliedBias = result;
            OnPropertyChanged(nameof(LastAppliedBias));
            OnPropertyChanged(nameof(LastBiasJson));
            OnPropertyChanged(nameof(LastBiasSucceeded));
        }

        private async Task EnterBiasAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CameraPanelViewModel));
            if (!await _biasGate.WaitAsync(0))
            {
                const string message = "이 카메라는 이미 BIAS 적용 또는 확인 중입니다.";
                AgentUiLog.Logger.Warning("[{AgentId}] BIAS 중복 실행 거부: {Reason}", _agentId, message);
                throw new InvalidOperationException(message);
            }
            IsBiasBusy = true;
        }

        private void ExitBias()
        {
            IsBiasBusy = false;
            _biasGate.Release();
        }

        /// <summary>입력한 GSK_LSB를 한 번 복원한다. 현재 온도에서 픽셀 목표를 다시 탐색하지 않는다.</summary>
        public async Task RestoreBiasAsync(string operation, byte value, CancellationToken cancellationToken)
        {
            await EnterBiasAsync();
            SetAppliedBias(null);
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _biasLifetimeCts.Token);
                await RestoreBiasCoreAsync(operation, value, linkedCancellation.Token);
            }
            finally
            {
                ExitBias();
            }
        }

        [RelayCommand(CanExecute = nameof(CanRestoreManualBias))]
        private async Task RestoreManualBiasAsync(CancellationToken cancellationToken)
        {
            if (!TryParseManualBiasValue(ManualBiasValueText, out byte value)
                || !BiasExecutionPolicy.IsBiasOperation(ManualBiasOperation))
            {
                SerialStatus = BiasText("Cam_ManualBiasInvalidInput");
                return;
            }

            string operation = ManualBiasOperation;
            bool entered = false;
            try
            {
                // 재시도 사이의 500ms에도 게이트를 유지해 다른 보정이 끼어들지 않게 한다.
                await EnterBiasAsync();
                entered = true;
                SetAppliedBias(null);
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _biasLifetimeCts.Token);
                BiasOperationResult result = await BiasOperationRunner.RunAsync(
                    ct => _dispatcher.InvokeAsync(() => RestoreBiasCoreAsync(operation, value, ct)).Task.Unwrap(),
                    () => LastBiasJson, null, false, BiasExecutionPolicy.DefaultAttemptTimeoutSeconds,
                    $"agent={_agentId} source=manual op={operation} GSK_LSB=0x{value:X2}",
                    message =>
                    {
                        if (message.StartsWith("[BIAS] NG", StringComparison.Ordinal))
                            AgentUiLog.Logger.Error("{BiasDiagnostic}", message);
                        else if (message.StartsWith("[BIAS] FAIL", StringComparison.Ordinal))
                            AgentUiLog.Logger.Warning("{BiasDiagnostic}", message);
                        else
                            AgentUiLog.Logger.Information("{BiasDiagnostic}", message);
                    },
                    linkedCancellation.Token);
                if (!result.IsSuccess)
                    SerialStatus = BiasText("Cam_ManualBiasRetryFailed", result.Message);
            }
            catch (OperationCanceledException)
            {
                if (entered) SetAppliedBias(null);
                SerialStatus = BiasText("Cam_ManualBiasCancelled");
            }
            catch (Exception ex)
            {
                if (entered) SetAppliedBias(null);
                SerialStatus = BiasText("Cam_ManualBiasFailed", ex.Message);
                AgentUiLog.Logger.Error(ex, "[{AgentId}] 수동 BIAS 복원 명령 실패: 모드={Operation}, 요청={Requested}", _agentId, operation, value);
            }
            finally
            {
                if (entered) ExitBias();
            }
        }

        private async Task RestoreBiasCoreAsync(string operation, byte value, CancellationToken ct)
        {
            SetAppliedBias(null);
            try
            {
                ct.ThrowIfCancellationRequested();
                ICameraSerialClient serial = _serial ?? throw new InvalidOperationException("BIAS 제어용 시리얼 연결이 없습니다.");
                (string mode, byte cint) = BiasMode(operation);
                SerialStatus = BiasText("Cam_ManualBiasApplying", mode, value);
                AgentUiLog.Logger.Information("[{AgentId}] 수동 BIAS 복원 시작: 모드={Mode}, GSK_LSB=0x{Requested:X2}", _agentId, mode, value);
                await serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, TintMsbValue, ct);
                ct.ThrowIfCancellationRequested();
                await serial.SetBiasRegisterAsync(CameraBiasRegister.TintLsb, TintLsbValue, ct);
                ct.ThrowIfCancellationRequested();
                await serial.SetBiasRegisterAsync(CameraBiasRegister.Cint, cint, ct);
                ct.ThrowIfCancellationRequested();
                await serial.SetBiasRegisterAsync(CameraBiasRegister.GskMsb, GskMsbValue, ct);
                ct.ThrowIfCancellationRequested();
                await serial.SetBiasRegisterAsync(CameraBiasRegister.Gfid, GfidValue, ct);
                ct.ThrowIfCancellationRequested();
                await serial.SetBiasAsync(value, ct);
                ct.ThrowIfCancellationRequested();
                byte actual = await serial.ReadBiasAsync(ct);
                ct.ThrowIfCancellationRequested();
                BiasLsb = $"0x{actual:X2}";
                if (actual != value)
                    throw new InvalidOperationException($"GSK_LSB 읽기 불일치: 요청 0x{value:X2}, 실제 0x{actual:X2}");

                var applied = new AppliedBiasResult(Guid.NewGuid().ToString("N"), operation, value,
                    BuildBiasJson(cint, value), BiasApplicationSource.Manual, DateTimeOffset.UtcNow);
                SetAppliedBias(applied);
                ct.ThrowIfCancellationRequested();
                SerialStatus = BiasText("Cam_ManualBiasApplied", mode, value, actual);
                AgentUiLog.Logger.Information("[{AgentId}] 수동 BIAS 복원 확인 완료: 적용ID={ApplicationId}, 모드={Mode}, 요청=0x{Requested:X2}, 읽기=0x{Actual:X2}",
                    _agentId, applied.ApplicationId, mode, value, actual);
            }
            catch (OperationCanceledException)
            {
                SetAppliedBias(null);
                SerialStatus = BiasText("Cam_ManualBiasCancelled");
                AgentUiLog.Logger.Warning("[{AgentId}] 수동 BIAS 복원 취소: 모드={Operation}, 요청=0x{Requested:X2}", _agentId, operation, value);
                throw;
            }
            catch (Exception ex)
            {
                SetAppliedBias(null);
                SerialStatus = BiasText("Cam_ManualBiasFailed", ex.Message);
                AgentUiLog.Logger.Error(ex, "[{AgentId}] 수동 BIAS 복원 실패: 모드={Operation}, 요청=0x{Requested:X2}, 원인={Reason}", _agentId, operation, value, ex.Message);
                throw;
            }
        }

        /// <summary>현재 적용 기록과 GSK_LSB 읽기값을 확인한다. 성공해도 적용 ID는 바꾸지 않는다.</summary>
        public async Task VerifyAppliedBiasAsync(string operation, CancellationToken cancellationToken)
        {
            await EnterBiasAsync();
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _biasLifetimeCts.Token);
                CancellationToken ct = linkedCancellation.Token;
                ct.ThrowIfCancellationRequested();
                AppliedBiasResult applied = LastAppliedBias ?? throw new InvalidOperationException("확인할 현재 BIAS 적용 기록이 없습니다.");
                if (!string.Equals(applied.Operation, operation, StringComparison.Ordinal))
                {
                    SetAppliedBias(null);
                    throw new InvalidOperationException($"BIAS 대역 불일치: 적용 {applied.Operation}, 요청 {operation}");
                }
                ICameraSerialClient serial = _serial ?? throw new InvalidOperationException("BIAS 제어용 시리얼 연결이 없습니다.");
                byte actual = await serial.ReadBiasAsync(ct);
                ct.ThrowIfCancellationRequested();
                BiasLsb = $"0x{actual:X2}";
                if (actual != applied.GskLsb)
                {
                    SetAppliedBias(null);
                    throw new InvalidOperationException($"GSK_LSB 읽기 불일치: 기록 0x{applied.GskLsb:X2}, 실제 0x{actual:X2}");
                }
                AgentUiLog.Logger.Information("[{AgentId}] 현재 BIAS 읽기 확인: 적용ID={ApplicationId}, 모드={Operation}, 출처={Source}, GSK_LSB=0x{Actual:X2}",
                    _agentId, applied.ApplicationId, operation, applied.Source, actual);
            }
            catch (OperationCanceledException)
            {
                SetAppliedBias(null);
                throw;
            }
            catch (Exception ex)
            {
                // 읽기 통신 실패는 재시도할 수 있도록 기록을 유지한다. 확정 불일치는 위에서 무효화한다.
                AgentUiLog.Logger.Warning(ex, "[{AgentId}] 현재 BIAS 확인 실패: 모드={Operation}, 원인={Reason}", _agentId, operation, ex.Message);
                throw;
            }
            finally
            {
                ExitBias();
            }
        }

        /// <summary>
        /// 목표 레벨 ±<see cref="BiasTargetTolerance"/> 안에 드는 바이어스 값을 탐색해 적용한다.
        /// 레시피 스텝이 목표를 지정하면 그 값이, 없으면 모드별 기본값이 들어온다.
        /// Cint는 카메라 특성이라 목표와 무관하게 모드별로 고정한다.
        /// </summary>
        private async Task RunAutoBiasAsync(string operation, string mode, double target, byte cint, CancellationToken cancellationToken)
        {
            // 수동 버튼과 원격 명령이 같은 카메라의 레지스터를 동시에 변경하지 못하게 한다.
            // 대기열을 만들지 않아 취소된 이전 요청이 뒤늦게 보정을 시작하는 것도 막는다.
            await EnterBiasAsync();
            SetAppliedBias(null);
            try
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _biasLifetimeCts.Token);
                CancellationToken ct = linkedCancellation.Token;
                ct.ThrowIfCancellationRequested();
                ICameraSerialClient serial = _serial ?? throw new InvalidOperationException("BIAS 제어용 시리얼 연결이 없습니다.");
                if (!double.IsFinite(target) || target <= 0 || target > 0x3FFF)
                    throw new ArgumentOutOfRangeException(nameof(target), target, "BIAS 목표는 0보다 크고 16383 이하여야 합니다.");

                double targetMin = target - BiasTargetTolerance;
                double targetMax = target + BiasTargetTolerance;
                AgentUiLog.Logger.Information("[{AgentId}] BIAS 보정 시작: 모드={Mode}, 목표={Target}, 허용범위={Minimum}~{Maximum}",
                    _agentId, mode, target, targetMin, targetMax);
                SerialStatus = $"BIAS: {target:F0} 탐색 중";

                await serial.SetBiasRegisterAsync(CameraBiasRegister.TintMsb, TintMsbValue, ct);
                await serial.SetBiasRegisterAsync(CameraBiasRegister.TintLsb, TintLsbValue, ct);
                await serial.SetBiasRegisterAsync(CameraBiasRegister.Cint, cint, ct);
                await serial.SetBiasRegisterAsync(CameraBiasRegister.GskMsb, GskMsbValue, ct);
                await serial.SetBiasRegisterAsync(CameraBiasRegister.Gfid, GfidValue, ct);
                BiasSearchResult result = await FindBiasInRangeAsync(targetMin, targetMax,
                    value => MeasureBiasAsync(serial, value, ct), ct);
                byte best = result.Value;
                double bestError = result.Error;
                if (!double.IsFinite(bestError) || bestError > 0)
                    throw new InvalidOperationException($"BIAS 목표 범위 미달: 목표 {target:F0}±{BiasTargetTolerance:F0}, 최선 0x{best:X2}, 범위 밖 오차 {bestError:F0}");

                // 탐색에서 본 값만 믿지 않고 최종 레지스터 적용 이후의 새 프레임으로 다시 확인한다.
                double finalLevel = await MeasureBiasAsync(serial, best, ct);
                ct.ThrowIfCancellationRequested();
                if (!double.IsFinite(finalLevel) || finalLevel < targetMin || finalLevel > targetMax)
                    throw new InvalidOperationException($"BIAS 최종 검증 실패: 목표 {target:F0}±{BiasTargetTolerance:F0}, 측정 {finalLevel:F0}, GSK_LSB=0x{best:X2}");

                SetAppliedBias(new AppliedBiasResult(Guid.NewGuid().ToString("N"), operation, best,
                    BuildBiasJson(cint, best), BiasApplicationSource.Automatic, DateTimeOffset.UtcNow));
                ct.ThrowIfCancellationRequested();
                SerialStatus = $"BIAS 완료: 0x{best:X2} (측정 {finalLevel:F0}, 오차 {Math.Abs(finalLevel - target):F0})";
                AgentUiLog.Logger.Information("[{AgentId}] BIAS 보정 검증 완료: 모드={Mode}, 목표={Target}, 측정={Measured}, GSK_LSB={Bias}, 오차={Error}",
                    _agentId, mode, target, finalLevel, best, Math.Abs(finalLevel - target));
            }
            catch (OperationCanceledException)
            {
                SetAppliedBias(null);
                SerialStatus = "BIAS 보정 취소";
                AgentUiLog.Logger.Warning("[{AgentId}] BIAS 보정 취소: 모드={Mode}, 목표={Target}", _agentId, mode, target);
                throw;
            }
            catch (Exception ex)
            {
                SetAppliedBias(null);
                SerialStatus = $"BIAS NG: {ex.Message}";
                AgentUiLog.Logger.Error(ex, "[{AgentId}] BIAS 보정 실패: 모드={Mode}, 목표={Target}, 원인={Reason}", _agentId, mode, target, ex.Message);
                throw;
            }
            finally
            {
                ExitBias();
            }
        }

        // 키 이름과 값 표기(GSK_MSB만 정수, 나머지는 대문자 hex 문자열)는 후처리 툴이 파싱하는
        // 계약이다 — 참고/AISEN_CODE/main.py:1593-1599, 1632-1646.
        private static string BuildBiasJson(byte cint, byte gskLsb)
            => System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    CINT = $"{cint:X2}",
                    TINT = $"{TintMsbValue:X}{TintLsbValue:X2}",
                    GSK_MSB = (int)GskMsbValue,
                    GSK_LSB = $"{gskLsb:X2}",
                    GFID = $"{GfidValue:X2}"
                },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        internal readonly record struct BiasSearchResult(byte Value, double Level, double Error)
        {
            public bool Success => Error == 0;
        }

        internal static async Task<BiasSearchResult> FindBiasInRangeAsync(
            double targetMin, double targetMax, Func<byte, Task<double>> measure, CancellationToken ct = default)
        {
            var readings = new Dictionary<byte, double>();
            async Task<double> Read(byte value)
            {
                ct.ThrowIfCancellationRequested();
                if (!readings.TryGetValue(value, out double result))
                {
                    result = await measure(value);
                    ct.ThrowIfCancellationRequested();
                    if (!double.IsFinite(result)) throw new InvalidOperationException("BIAS 측정값이 유효하지 않습니다.");
                    readings[value] = result;
                }
                return result;
            }

            byte best = 0;
            double bestError = double.MaxValue;
            void Consider(byte value, double level)
            {
                double error = level < targetMin ? targetMin - level : level > targetMax ? level - targetMax : 0;
                if (error < bestError) { best = value; bestError = error; }
            }

            double atZero = await Read(0);
            Consider(0, atZero);
            if (bestError == 0) return new BiasSearchResult(best, readings[best], bestError);
            double atFull = await Read(byte.MaxValue);
            Consider(byte.MaxValue, atFull);
            if (bestError == 0) return new BiasSearchResult(best, readings[best], bestError);
            bool increasing = atFull >= atZero;

            for (int value = 0; value <= 0xF0; value += 0x10)
            {
                Consider((byte)value, await Read((byte)value));
                if (bestError == 0) return new BiasSearchResult(best, readings[best], bestError);
            }
            int coarse = best & 0xF0;
            for (int value = coarse; value <= Math.Min(coarse + 0x0F, 0xFF); value++)
            {
                Consider((byte)value, await Read((byte)value));
                if (bestError == 0) return new BiasSearchResult(best, readings[best], bestError);
            }

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
            return new BiasSearchResult(best, readings[best], bestError);
        }

        private async Task<double> MeasureBiasAsync(ICameraSerialClient serial, byte value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await serial.SetBiasAsync(value, ct);
            await Task.Delay(100, ct);
            // snapshot의 타임아웃 폴백과 write 이전/안정화 중 프레임을 측정에서 제외한다.
            DateTimeOffset previousFrameAt = DateTimeOffset.Now;
            var measurements = new double[5];
            for (int sample = 0; sample < measurements.Length; sample++)
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                ThermalFrame? frame;
                do
                {
                    frame = await _runtime.CaptureSnapshotAsync(
                        maxAge: TimeSpan.Zero,
                        nextFrameTimeout: TimeSpan.FromSeconds(2),
                        ct: ct);
                    // CameraRuntime은 취소/시간 초과 때 과거 프레임을 반환할 수 있다.
                    ct.ThrowIfCancellationRequested();
                    if (frame is not null && frame.Timestamp > previousFrameAt) break;
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2))
                        throw new TimeoutException($"BIAS 새 프레임 대기 실패: GSK_LSB=0x{value:X2}, 마지막 프레임={frame?.Timestamp:O}");
                    await Task.Delay(1, ct);
                } while (true);

                if (!frame.IsRadiometric || frame.Pixels.Length == 0)
                    throw new InvalidOperationException("BIAS 측정에는 비어 있지 않은 Y16 원본 프레임이 필요합니다.");
                previousFrameAt = frame.Timestamp;
                long sum = 0;
                foreach (ushort pixel in frame.Pixels) sum += pixel & 0x3FFF;
                measurements[sample] = (double)sum / frame.Pixels.Length;
                await Task.Delay(30, ct);
            }
            Array.Sort(measurements);
            double level = measurements[2];
            AgentUiLog.Logger.Information("[{AgentId}] BIAS LSB 0x{Lsb:X2} → {Level}",
                _agentId, value, level);
            return level;
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
            await ReadOutputFormatAsync();
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
            if (_disposed) return;
            _disposed = true;
            SetAppliedBias(null);
            OnPropertyChanged(nameof(CanEditBias));
            RunBiasLowCommand.NotifyCanExecuteChanged();
            RunBiasMidCommand.NotifyCanExecuteChanged();
            RunBiasHighCommand.NotifyCanExecuteChanged();
            RestoreManualBiasCommand.NotifyCanExecuteChanged();
            _biasLifetimeCts.Cancel();
            _infoPollCts?.Cancel();
            _infoPollCts?.Dispose();
            _runtime.FrameReady -= OnFrameReady;
            _runtime.StatusChanged -= OnStatusChanged;
            _serial?.Dispose();
            _biasLifetimeCts.Dispose();
        }
    }
}
