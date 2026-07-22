using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class ManualControlViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;

        [ObservableProperty] private string _statusMessage = "대기";
        [ObservableProperty] private int _servoXPosition;
        [ObservableProperty] private int _servoYPosition;
        [ObservableProperty] private int _currentPoint;
        [ObservableProperty] private bool _servoXBusy;
        [ObservableProperty] private bool _servoYBusy;
        [ObservableProperty] private float _fanSpeedHz;

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

        public int[] PointNumbers { get; } = Enumerable.Range(1, 20).ToArray();

        public ManualControlViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await PollAsync();
            _timer.Start();
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
            var plc = AppServices.PlcController;
            if (plc == null) return;
            try
            {
                var s = await plc.ReadStatusAsync();
                ServoXPosition = s.ServoXPosition;
                ServoYPosition = s.ServoYPosition;
                CurrentPoint = s.CurrentPoint;
                ServoXBusy = s.ServoXBusy;
                ServoYBusy = s.ServoYBusy;
                FanSpeedHz = s.FanSpeedHz;
            }
            catch (Exception ex)
            {
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
