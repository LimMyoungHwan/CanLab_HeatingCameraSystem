using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
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

    public partial class ManualControlViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;
        private readonly HashSet<string> _subscribedAgentIds = new();

        public ObservableCollection<CameraTileModel> Cameras { get; } = new();

        [ObservableProperty] private CameraTileModel? _selectedCamera;

        // 0=컬러(iron), 1=그레이스케일 — XAML 콤보 아이템 순서와 일치.
        [ObservableProperty] private int _colorMapIndex = LivePreviewColorMode.Grayscale ? 1 : 0;

        [ObservableProperty] private string _statusMessage = "대기";
        [ObservableProperty] private int _servoXPosition;
        [ObservableProperty] private int _servoYPosition;
        [ObservableProperty] private int _currentPoint;
        [ObservableProperty] private bool _servoXBusy;
        [ObservableProperty] private bool _servoYBusy;
        [ObservableProperty] private float _fanSpeedHz;

        [ObservableProperty] private int _absoluteTargetX;
        [ObservableProperty] private int _absoluteTargetY;
        [ObservableProperty] private int _relativeStepX;
        [ObservableProperty] private int _relativeStepY;

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

        private bool _blackBodyPolling;

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

        partial void OnCooler1stChanged(bool value) => _ = EquipmentAsync(PlcEquipment.Cooler1st, value);
        partial void OnCooler2ndChanged(bool value) => _ = EquipmentAsync(PlcEquipment.Cooler2nd, value);
        partial void OnCoolerRoomChanged(bool value) => _ = EquipmentAsync(PlcEquipment.CoolerRoom, value);
        partial void OnBlower1Changed(bool value) => _ = EquipmentAsync(PlcEquipment.Blower1, value);
        partial void OnBlower2Changed(bool value) => _ = EquipmentAsync(PlcEquipment.Blower2, value);
        partial void OnChillerChanged(bool value) => _ = EquipmentAsync(PlcEquipment.Chiller, value);
        partial void OnDoorLockChanged(bool value) => _ = EquipmentAsync(PlcEquipment.DoorLock, value);
        partial void OnLightingChanged(bool value) => _ = EquipmentAsync(PlcEquipment.Lighting, value);
        partial void OnPairGlassChanged(bool value) => _ = EquipmentAsync(PlcEquipment.PairGlass, value);

        partial void OnHumidityControlChanged(bool value) => _ = RunAsync(p => p.SetHumidityControlAsync(value), "습도제어");

        [RelayCommand]
        private Task StartChamber() => RunAsync(p => p.StartChamberAsync(), "챔버 시작");

        [RelayCommand]
        private Task StopChamber() => RunAsync(p => p.StopChamberAsync(), "챔버 정지");

        [RelayCommand]
        private Task EmergencyStop() => RunAsync(p => p.TriggerEmergencyStopAsync(), "비상정지");

        [RelayCommand]
        private Task HomeX() => RunAsync(p => p.HomeAsync(ServoAxis.X), "X축 원점");

        [RelayCommand]
        private Task HomeY() => RunAsync(p => p.HomeAsync(ServoAxis.Y), "Y축 원점");

        [RelayCommand]
        private Task MoveToPoint(int index) => RunAsync(p => p.MoveServoToPositionAsync(index), $"{index}포인트 이동");

        [RelayCommand]
        private Task MoveAbsolute(string axis) => axis == "Y"
            ? RunAsync(p => p.MoveToCoordinateAsync(ServoXPosition, AbsoluteTargetY), "Y 절대이동")
            : RunAsync(p => p.MoveToCoordinateAsync(AbsoluteTargetX, ServoYPosition), "X 절대이동");

        [RelayCommand]
        private Task MoveRelative(string dir)
        {
            int x = ServoXPosition, y = ServoYPosition;
            switch (dir)
            {
                case "X+": x += RelativeStepX; break;
                case "X-": x -= RelativeStepX; break;
                case "Y+": y += RelativeStepY; break;
                case "Y-": y -= RelativeStepY; break;
                default: return Task.CompletedTask;
            }
            return RunAsync(p => p.MoveToCoordinateAsync(x, y), $"상대이동 {dir}");
        }

        [RelayCommand]
        private Task ApplyBlackBody1() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(0, BlackBody1Target), "흑체1 온도");

        [RelayCommand]
        private Task ApplyBlackBody2() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(1, BlackBody2Target), "흑체2 온도");

        private async Task PublishCameraCommandAsync(CameraTileModel tile, string op)
        {
            if (tile == null || AppServices.NatsService == null) return;

            tile.LastAckStatus = "⏳ 전송됨…";
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
                tile.LastAckStatus = $"✘ 전송 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ManualControl] Publish failed: {ex.Message}");
            }
        }

        [RelayCommand] private Task SendRun(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Run);
        [RelayCommand] private Task SendStop(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Stop);
        [RelayCommand] private Task SendShutterOpen(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.ShutterOpen);
        [RelayCommand] private Task SendShutterClose(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.ShutterClose);
        [RelayCommand] private Task SendCapture(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Capture);
        [RelayCommand] private Task SendNuc(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.Nuc);
        [RelayCommand] private Task SendSaveConfig(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.SaveConfig);
        [RelayCommand] private Task SendRefreshInfo(CameraTileModel tile) => PublishCameraCommandAsync(tile, CameraControlOps.RefreshInfo);

        public Task Jog(ServoAxis axis, bool positive, bool on)
        {
            var plc = AppServices.PlcController;
            if (plc == null) return Task.CompletedTask;
            return SafeAsync(() => plc.JogAsync(axis, positive, on));
        }

        private Task EquipmentAsync(PlcEquipment equipment, bool on)
            => RunAsync(p => p.SetEquipmentAsync(equipment, on), equipment.ToString());

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
            }

            await PollBlackBodyAsync();
        }

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

        private async Task RunBlackBodyAsync(Func<IBlackBodyController, Task> action, string label)
        {
            var bb = AppServices.BlackBodyController;
            if (bb == null) { StatusMessage = "흑체 컨트롤러 미초기화"; return; }
            try
            {
                await action(bb);
                StatusMessage = $"{label} 적용됨";
            }
            catch (Exception ex)
            {
                StatusMessage = $"{label} 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ManualControl] {ex.Message}");
            }
        }

        private async Task RunAsync(Func<IPlcController, Task> action, string label)
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = "PLC 미초기화"; return; }
            try
            {
                await action(plc);
                StatusMessage = $"{label} 실행됨";
            }
            catch (Exception ex)
            {
                StatusMessage = $"{label} 오류: {ex.Message}";
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
