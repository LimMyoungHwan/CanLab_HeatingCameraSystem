using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>수동 조작 화면 카메라 타일 1칸. 라이브 프레임과 마지막 카메라 제어 ACK 상태를 보여 준다.</summary>
    public partial class CameraTileModel : ObservableObject
    {
        public string AgentId { get; }
        public int CameraIndex { get; }
        public string Title { get; }

        [ObservableProperty] private BitmapSource? _liveImage;
        [ObservableProperty] private string _lastAckStatus = "";

        public CameraTileModel(string agentId, int cameraIndex, string title)
        {
            AgentId = agentId;
            CameraIndex = cameraIndex;
            Title = title;
        }
    }

    /// <summary>
    /// 수동 조작 화면. 챔버 기동/정지, 서보 이동·JOG, 온·습도/램프/팬/흑체 설정, 개별 카메라
    /// 제어(NATS)를 담당한다. 서보 위치 등 현재값은 직접 PLC를 읽지 않고 공용 PlcStatusService
    /// 스냅샷을 1초 타이머로 복사한다. NATS 콜백은 백그라운드 스레드로 오므로 UI 갱신은
    /// Dispatcher.Invoke로 마샬링한다.
    /// </summary>
    public partial class ManualControlViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;
        private readonly HashSet<string> _subscribedAgentIds = new();

        public ObservableCollection<CameraTileModel> Cameras { get; } = new();

        [ObservableProperty] private CameraTileModel? _selectedCamera;

        // 0=컬러(iron), 1=그레이스케일 — XAML 콤보 아이템 순서와 일치.
        [ObservableProperty] private int _colorMapIndex = LivePreviewColorMode.Grayscale ? 1 : 0;

        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Common_Idle"];
        [ObservableProperty] private float _servoXPosition;
        [ObservableProperty] private float _servoYPosition;
        [ObservableProperty] private int _currentPoint;
        [ObservableProperty] private bool _servoXBusy;
        [ObservableProperty] private bool _servoYBusy;
        [ObservableProperty] private float _fanSpeedHz;

        // mm 단위(PLC 워드 0.1mm 분해능)
        [ObservableProperty] private float _absoluteTargetX;
        [ObservableProperty] private float _absoluteTargetY;
        [ObservableProperty] private float _relativeStepX;
        [ObservableProperty] private float _relativeStepY;

        [ObservableProperty] private bool _cooler1st;
        [ObservableProperty] private bool _cooler2nd;
        [ObservableProperty] private bool _coolerRoom;
        [ObservableProperty] private bool _blower1;
        [ObservableProperty] private bool _blower2;
        [ObservableProperty] private bool _chiller;
        [ObservableProperty] private bool _doorLock;
        [ObservableProperty] private bool _lighting;
        [ObservableProperty] private bool _pairGlass;
        [ObservableProperty] private bool _humidityControl;

        [ObservableProperty] private float _blackBody1Target = 25f;
        [ObservableProperty] private float _blackBody2Target = 25f;
        [ObservableProperty] private float _blackBody1Current;
        [ObservableProperty] private float _blackBody2Current;

        // 온/습도 제어 · 온도 램프 · 모터/팬 (PLC 설정 화면에서 이동)
        [ObservableProperty] private float _targetTemperature = 25f;
        [ObservableProperty] private float _targetHumidity = 50f;
        [ObservableProperty] private float _rampTargetTemperature = 25f;
        [ObservableProperty] private int _rampMinutes = 10;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartRampCommand))]
        private bool _isRamping;

        [ObservableProperty] private int _servoSpeedPercent = 100;
        // FanSpeedHz는 스냅샷 현재값이므로 목표값은 별도 속성으로 둔다(1초 폴링에 덮이지 않게).
        [ObservableProperty] private float _fanSpeedTargetHz;

        private CancellationTokenSource? _rampCts;
        private bool _blackBodyPolling;
        private bool _syncingEquipmentFromPlc;

        // AppServices는 RecipeEngineSettings를 노출하지 않으므로 필요한 값을 로컬 기본값으로 둔다.
        private const int RampStepIntervalSeconds = 30;

        public int[] PointNumbers { get; } = Enumerable.Range(1, 20).ToArray();

        public ManualControlViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await PollAsync();
            _timer.Start();

            SubscribeCameraServices();

            LivePreviewColorMode.Changed += OnColorModeChanged;
        }

        partial void OnColorMapIndexChanged(int value) => LivePreviewColorMode.SetGrayscale(value == 1);

        private void OnColorModeChanged() =>
            Application.Current?.Dispatcher.Invoke(() => ColorMapIndex = LivePreviewColorMode.Grayscale ? 1 : 0);

        private void SubscribeCameraServices()
        {
            var nats = AppServices.NatsService;
            if (nats == null) return;

            try
            {
                nats.SubscribeAgentStatusAsync(OnAgentStatus);
                nats.SubscribeLiveFrameAsync(OnLiveFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManualControl] NATS subscribe failed: {ex.Message}");
            }
        }

        private void OnAgentStatus(AgentStatusMessage msg)
        {
            if (string.IsNullOrEmpty(msg.AgentId)) return;
            Application.Current?.Dispatcher.Invoke(() => EnsureTile(msg.AgentId, msg.CameraIndex));
        }

        /// <summary>
        /// (AgentId, CameraIndex) 타일을 찾거나 새로 만든다. 새 Agent면 카메라 제어 ACK 구독을
        /// 1회만 등록한다(해제 API가 없으므로 중복 구독 금지). UI 스레드에서만 호출해야 한다.
        /// </summary>
        private CameraTileModel EnsureTile(string agentId, int cameraIndex)
        {
            var tile = Cameras.FirstOrDefault(c => c.AgentId == agentId && c.CameraIndex == cameraIndex);
            if (tile == null)
            {
                tile = new CameraTileModel(agentId, cameraIndex, $"{agentId} (cam {cameraIndex})");
                Cameras.Add(tile);
                SelectedCamera ??= tile;

                if (_subscribedAgentIds.Add(agentId))
                {
                    try
                    {
                        AppServices.NatsService?.SubscribeCameraControlAckAsync(agentId, OnCameraAck);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ManualControl] Ack subscribe failed for {agentId}: {ex.Message}");
                    }
                }
            }
            return tile;
        }

        private void OnCameraAck(CameraControlAckMessage ack)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var tile = Cameras.FirstOrDefault(c => c.AgentId == ack.AgentId && c.CameraIndex == ack.CameraIndex);
                if (tile != null)
                {
                    tile.LastAckStatus = ack.IsSuccess ? $"✔ {ack.Op} {ack.Message}" : $"✘ {ack.Op} {ack.Message}";
                }
            });
        }

        private void OnLiveFrame(LiveFrameMessage msg)
        {
            if (msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;

            BitmapSource? bmp = Decode(msg.ImageBytes);
            if (bmp is null) return;
            bmp = LivePreviewColorMode.Apply(bmp);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var tile = EnsureTile(msg.AgentId, msg.CameraIndex);
                tile.LiveImage = bmp;
            });
        }

        /// <summary>JPEG 바이트를 디코드한다. Freeze로 스레드 간 전달을 허용하며, 손상 데이터는 null을 반환한다.</summary>
        private static BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(jpeg);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        partial void OnCooler1stChanged(bool value) => SyncEquipment(PlcEquipment.Cooler1st, value);
        partial void OnCooler2ndChanged(bool value) => SyncEquipment(PlcEquipment.Cooler2nd, value);
        partial void OnCoolerRoomChanged(bool value) => SyncEquipment(PlcEquipment.CoolerRoom, value);
        partial void OnBlower1Changed(bool value) => SyncEquipment(PlcEquipment.Blower1, value);
        partial void OnBlower2Changed(bool value) => SyncEquipment(PlcEquipment.Blower2, value);
        partial void OnChillerChanged(bool value) => SyncEquipment(PlcEquipment.Chiller, value);
        partial void OnDoorLockChanged(bool value) => SyncEquipment(PlcEquipment.DoorLock, value);
        partial void OnLightingChanged(bool value) => SyncEquipment(PlcEquipment.Lighting, value);
        partial void OnPairGlassChanged(bool value) => SyncEquipment(PlcEquipment.PairGlass, value);

        partial void OnHumidityControlChanged(bool value) => _ = RunAsync(p => p.SetHumidityControlAsync(value), LocalizationManager.Instance["Manual_HumidityControlLabel"]);

        /// <summary>챔버 기동.</summary>
        [RelayCommand]
        private Task StartChamber() => RunAsync(p => p.StartChamberAsync(), LocalizationManager.Instance["Manual_StartChamber"]);

        /// <summary>챔버 정지.</summary>
        [RelayCommand]
        private Task StopChamber() => RunAsync(p => p.StopChamberAsync(), LocalizationManager.Instance["Manual_StopChamber"]);

        /// <summary>비상정지 트리거.</summary>
        [RelayCommand]
        private Task EmergencyStop()
        {
            AppServices.RecipeEngine?.RequestEmergencyStop();
            return RunAsync(p => p.TriggerEmergencyStopAsync(), LocalizationManager.Instance["Equip_EStop"]);
        }

        /// <summary>X축 원점 복귀.</summary>
        [RelayCommand]
        private Task HomeX() => RunAsync(p => p.HomeAsync(ServoAxis.X), LocalizationManager.Instance["Manual_HomeXLabel"]);

        /// <summary>Y축 원점 복귀.</summary>
        [RelayCommand]
        private Task HomeY() => RunAsync(p => p.HomeAsync(ServoAxis.Y), LocalizationManager.Instance["Manual_HomeYLabel"]);

        /// <summary>저장된 포인트 번호(1~20)로 서보를 이동시킨다.</summary>
        [RelayCommand]
        private Task MoveToPoint(int index) => RunAsync(p => p.MoveServoToPositionAsync(index), L("Manual_MovePointLabel", index));

        /// <summary>한 축만 절대좌표(mm)로 이동한다. 나머지 축은 현재 위치를 유지한다.</summary>
        [RelayCommand]
        private Task MoveAbsolute(string axis) => axis == "Y"
            ? RunAsync(p => p.MoveToCoordinateAsync(ServoXPosition, AbsoluteTargetY), LocalizationManager.Instance["Manual_MoveYAbs"])
            : RunAsync(p => p.MoveToCoordinateAsync(AbsoluteTargetX, ServoYPosition), LocalizationManager.Instance["Manual_MoveXAbs"]);

        /// <summary>현재 위치 기준 상대 이동(mm). dir은 "X+"/"X-"/"Y+"/"Y-"만 인정한다.</summary>
        [RelayCommand]
        private Task MoveRelative(string dir)
        {
            float x = ServoXPosition, y = ServoYPosition;
            switch (dir)
            {
                case "X+": x += RelativeStepX; break;
                case "X-": x -= RelativeStepX; break;
                case "Y+": y += RelativeStepY; break;
                case "Y-": y -= RelativeStepY; break;
                default: return Task.CompletedTask;
            }
            return RunAsync(p => p.MoveToCoordinateAsync(x, y), L("Manual_RelMoveLabel", dir));
        }

        /// <summary>목표 온도(℃)를 목표값·제어값 두 워드에 함께 쓴다.</summary>
        [RelayCommand]
        private Task ApplyTemperature() => RunAsync(async p =>
        {
            await p.SetTargetTemperatureAsync(TargetTemperature);
            await p.SetControlTemperatureAsync(TargetTemperature);
        }, LocalizationManager.Instance["Manual_TargetTempLabel"]);

        /// <summary>목표 습도(%)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyHumidity() => RunAsync(p => p.SetTargetHumidityAsync(TargetHumidity), LocalizationManager.Instance["Manual_TargetHumLabel"]);

        private bool CanStartRamp() => !IsRamping;

        /// <summary>
        /// 현재 온도에서 목표 온도까지 <see cref="RampMinutes"/>분에 걸쳐 선형 스텝으로 올린다
        /// (히터 급출력 방지). 실행 중 재시작은 CanExecute로 막고, 정지는 <see cref="StopRamp"/>가 취소한다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartRamp))]
        private async Task StartRampAsync()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }

            _rampCts?.Dispose();
            _rampCts = new CancellationTokenSource();
            IsRamping = true;
            try
            {
                float start = await plc.GetCurrentTemperatureAsync();
                _rampCts.Token.ThrowIfCancellationRequested();
                var controller = new TemperatureRampController(plc, RampStepIntervalSeconds);
                var rampProgress = new Progress<string>(message => StatusMessage = message);
                await controller.RampAsync(start, RampTargetTemperature, RampMinutes, rampProgress, _rampCts.Token);
                StatusMessage = LocalizationManager.Instance["Manual_RampDone"];
            }
            catch (OperationCanceledException)
            {
                StatusMessage = LocalizationManager.Instance["Manual_RampStopped"];
            }
            catch (Exception ex)
            {
                StatusMessage = L("Manual_RampError", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[ManualControl] {ex.Message}");
            }
            finally
            {
                IsRamping = false;
                _rampCts?.Dispose();
                _rampCts = null;
            }
        }

        /// <summary>진행 중인 온도 램프를 취소한다.</summary>
        [RelayCommand]
        private void StopRamp() => _rampCts?.Cancel();

        /// <summary>서보 속도(%)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyServoSpeed() => RunAsync(p => p.SetServoSpeedAsync(ServoSpeedPercent), LocalizationManager.Instance["Manual_ServoSpeedLabel"]);

        /// <summary>팬 속도 목표값(Hz)을 적용한다.</summary>
        [RelayCommand]
        private Task ApplyFanSpeed() => RunAsync(p => p.SetFanSpeedAsync(FanSpeedTargetHz), LocalizationManager.Instance["Status_FanSpeedLabel"]);

        /// <summary>흑체 1 목표 온도(℃)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyBlackBody1() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(0, BlackBody1Target), LocalizationManager.Instance["Plc_Bb1Temp"]);

        /// <summary>흑체 2 목표 온도(℃)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyBlackBody2() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(1, BlackBody2Target), LocalizationManager.Instance["Plc_Bb2Temp"]);

        /// <summary>
        /// 카메라 제어 명령(Run/Stop/셔터/NUC 등)을 해당 Agent에 발행한다.
        /// 처리 결과는 ACK 구독이 타일의 <see cref="CameraTileModel.LastAckStatus"/>로 보고한다.
        /// </summary>
        private async Task PublishCameraCommandAsync(CameraTileModel tile, string op)
        {
            if (tile == null || AppServices.NatsService == null) return;

            tile.LastAckStatus = LocalizationManager.Instance["Manual_Sending"];
            var msg = new CameraControlMessage
            {
                AgentId = tile.AgentId,
                CameraIndex = tile.CameraIndex,
                Op = op,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await AppServices.NatsService.PublishCameraControlAsync(msg);
            }
            catch (Exception ex)
            {
                tile.LastAckStatus = L("Manual_SendFailed", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[ManualControl] Publish failed: {ex.Message}");
            }
        }

        [RelayCommand] private Task SendRun(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Run);
        [RelayCommand] private Task SendStop(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Stop);
        [RelayCommand] private Task SendShutterOpen(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.ShutterOpen);
        [RelayCommand] private Task SendShutterClose(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.ShutterClose);
        /// <summary>수동 캡처 요청. 카메라 제어 채널이 아닌 캡처 명령 채널로 발행하며 Source는 Manual로 기록된다.</summary>
        [RelayCommand]
        private async Task SendCapture(CameraTileModel tile)
        {
            if (tile == null || AppServices.NatsService == null) return;
            tile.LastAckStatus = LocalizationManager.Instance["Manual_CaptureRequesting"];
            try
            {
                await AppServices.NatsService.PublishCaptureCommandAsync(new CaptureCommandMessage
                {
                    TargetAgentId = tile.AgentId,
                    Source = CaptureSource.Manual,
                    RecipeStepId = string.Empty,
                    Timestamp = DateTime.UtcNow
                });
                tile.LastAckStatus = LocalizationManager.Instance["Manual_CaptureSent"];
            }
            catch (Exception ex)
            {
                tile.LastAckStatus = L("Manual_CaptureFailed", ex.Message);
            }
        }
        [RelayCommand] private Task SendNuc(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Nuc);
        [RelayCommand] private Task SendBiasLow(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.BiasLow);
        [RelayCommand] private Task SendBiasMid(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.BiasMid);
        [RelayCommand] private Task SendBiasHigh(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.BiasHigh);
        [RelayCommand] private Task SendSaveConfig(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.SaveConfig);
        [RelayCommand] private Task SendRefreshInfo(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.RefreshInfo);

        /// <summary>JOG 이동. 버튼 누름(on=true)/뗌(on=false)을 View 코드비하인드가 직접 호출한다.</summary>
        public Task Jog(ServoAxis axis, bool positive, bool on)
        {
            var plc = AppServices.PlcController;
            if (plc == null) return Task.CompletedTask;
            return SafeAsync(() => plc.JogAsync(axis, positive, on));
        }

        private void SyncEquipment(PlcEquipment equipment, bool on)
        {
            if (!_syncingEquipmentFromPlc) _ = EquipmentAsync(equipment, on);
        }

        private Task EquipmentAsync(PlcEquipment equipment, bool on)
            => RunAsync(p => p.SetEquipmentAsync(equipment, on), equipment.ToString());

        /// <summary>
        /// 1초 타이머 틱. 서보 현재값은 공용 PlcStatusService 스냅샷에서 복사하고(직접 PLC 판독 없음),
        /// 흑체 현재값만 별도로 폴링한다.
        /// </summary>
        private async Task PollAsync()
        {
            var s = AppServices.PlcStatus?.Snapshot;
            if (s != null)
            {
                ServoXPosition = s.ServoXPosition;
                ServoYPosition = s.ServoYPosition;
                CurrentPoint = s.CurrentPoint;
                ServoXBusy = s.ServoXBusy;
                ServoYBusy = s.ServoYBusy;
                FanSpeedHz = s.FanSpeedHz;
                _syncingEquipmentFromPlc = true;
                try
                {
                    Cooler1st = s.Cooler1st;
                    Cooler2nd = s.Cooler2nd;
                    CoolerRoom = s.CoolerRoom;
                    Blower1 = s.Blower1;
                    Blower2 = s.Blower2;
                    Chiller = s.Chiller;
                    DoorLock = s.DoorLock;
                    Lighting = s.Lighting;
                    PairGlass = s.PairGlass;
                }
                finally { _syncingEquipmentFromPlc = false; }
            }

            await PollBlackBodyAsync();
        }

        /// <summary>흑체 현재 온도(℃)를 폴링한다. 이전 폴링이 끝나지 않았으면 겹치지 않게 건너뛴다.</summary>
        private async Task PollBlackBodyAsync()
        {
            var bb = AppServices.BlackBodyController;
            if (bb == null || _blackBodyPolling) return;
            _blackBodyPolling = true;
            try
            {
                if (bb.Count > 0) BlackBody1Current = await bb.GetCurrentTemperatureAsync(0);
                if (bb.Count > 1) BlackBody2Current = await bb.GetCurrentTemperatureAsync(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManualControl] blackbody poll: {ex.Message}");
            }
            finally { _blackBodyPolling = false; }
        }

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Instance[key], args);

        /// <summary>흑체 제어 공통 실행기. 미초기화·실패를 <see cref="StatusMessage"/>로 보고한다.</summary>
        private async Task RunBlackBodyAsync(Func<IBlackBodyController, Task> action, string label)
        {
            var bb = AppServices.BlackBodyController;
            if (bb == null) { StatusMessage = LocalizationManager.Instance["Plc_BbNotInit"]; return; }
            try
            {
                await action(bb);
                StatusMessage = L("Plc_LabelApplied", label);
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_LabelError", label, ex.Message);
                System.Diagnostics.Debug.WriteLine($"[ManualControl] {ex.Message}");
            }
        }

        /// <summary>PLC 제어 공통 실행기. 미초기화·실패를 <see cref="StatusMessage"/>로 보고한다.</summary>
        private async Task RunAsync(Func<IPlcController, Task> action, string label)
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }
            try
            {
                await action(plc);
                StatusMessage = L("Manual_LabelExecuted", label);
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_LabelError", label, ex.Message);
                System.Diagnostics.Debug.WriteLine($"[ManualControl] {ex.Message}");
            }
        }

        private static async Task SafeAsync(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ManualControl] jog: {ex.Message}"); }
        }
    }
}
