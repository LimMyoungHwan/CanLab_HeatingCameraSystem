using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class PointCoordRow : ObservableObject
    {
        public int Index { get; init; }

        [ObservableProperty] private int _x;
        [ObservableProperty] private int _y;
    }

    public partial class PlcControlSettingsViewModel : ObservableObject
    {
        [ObservableProperty] private float _targetTemperature = 25f;
        [ObservableProperty] private float _targetHumidity = 50f;
        [ObservableProperty] private bool _humidityControl;
        [ObservableProperty] private float _blackBody1Target = 25f;
        [ObservableProperty] private float _blackBody2Target = 25f;
        [ObservableProperty] private int _servoSpeedPercent = 100;
        [ObservableProperty] private float _fanSpeedHz;

        [ObservableProperty] private float _overheatLimit;
        [ObservableProperty] private float _coolerRoomBoundary;
        [ObservableProperty] private float _cooler2ndBoundary;
        [ObservableProperty] private int _coolerDelayMinutes;
        [ObservableProperty] private float _bypassBoundary;
        [ObservableProperty] private float _mfcMinOutput;
        [ObservableProperty] private float _mfcMaxOutput;
        [ObservableProperty] private float _pairGlassBoundary;

        [ObservableProperty] private string _statusMessage = "대기";

        public ObservableCollection<PointCoordRow> Points { get; } = new();

        public PlcControlSettingsViewModel()
        {
            for (int i = 1; i <= 20; i++)
                Points.Add(new PointCoordRow { Index = i });
        }

        [RelayCommand]
        private Task ApplyTemperature() => RunAsync(p => p.SetTargetTemperatureAsync(TargetTemperature), "타겟 온도");

        [RelayCommand]
        private Task ApplyHumidity() => RunAsync(p => p.SetTargetHumidityAsync(TargetHumidity), "타겟 습도");

        [RelayCommand]
        private Task ApplyHumidityControl() => RunAsync(p => p.SetHumidityControlAsync(HumidityControl), "습도제어");

        [RelayCommand]
        private Task ApplyBlackBody1() => RunAsync(p => p.SetBlackBodyTemperatureAsync(0, BlackBody1Target), "흑체1 온도");

        [RelayCommand]
        private Task ApplyBlackBody2() => RunAsync(p => p.SetBlackBodyTemperatureAsync(1, BlackBody2Target), "흑체2 온도");

        [RelayCommand]
        private Task ApplyServoSpeed() => RunAsync(p => p.SetServoSpeedAsync(ServoSpeedPercent), "서보 속도");

        [RelayCommand]
        private Task ApplyFanSpeed() => RunAsync(p => p.SetFanSpeedAsync(FanSpeedHz), "팬 속도");

        [RelayCommand]
        private async Task LoadPoints()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = "PLC 미초기화"; return; }
            try
            {
                foreach (var row in Points)
                {
                    var (x, y) = await plc.GetPointCoordinateAsync(row.Index);
                    row.X = x;
                    row.Y = y;
                }
                StatusMessage = "포인트 좌표 불러옴";
            }
            catch (Exception ex)
            {
                StatusMessage = $"불러오기 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task SavePoints()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = "PLC 미초기화"; return; }
            try
            {
                foreach (var row in Points)
                    await plc.SetPointCoordinateAsync(row.Index, row.X, row.Y);
                StatusMessage = "포인트 좌표 저장됨";
            }
            catch (Exception ex)
            {
                StatusMessage = $"저장 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task LoadAdmin()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = "PLC 미초기화"; return; }
            try
            {
                var a = (await plc.ReadStatusAsync()).Admin;
                OverheatLimit = a.OverheatLimit;
                CoolerRoomBoundary = a.CoolerRoomBoundary;
                Cooler2ndBoundary = a.Cooler2ndBoundary;
                CoolerDelayMinutes = a.CoolerDelayMinutes;
                BypassBoundary = a.BypassBoundary;
                MfcMinOutput = a.MfcMinOutput;
                MfcMaxOutput = a.MfcMaxOutput;
                PairGlassBoundary = a.PairGlassBoundary;
                StatusMessage = "관리자 설정 불러옴";
            }
            catch (Exception ex)
            {
                StatusMessage = $"불러오기 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        [RelayCommand]
        private Task SaveAdmin() => RunAsync(p => p.WriteAdminSettingsAsync(new PlcAdminSettings
        {
            OverheatLimit = OverheatLimit,
            CoolerRoomBoundary = CoolerRoomBoundary,
            Cooler2ndBoundary = Cooler2ndBoundary,
            CoolerDelayMinutes = CoolerDelayMinutes,
            BypassBoundary = BypassBoundary,
            MfcMinOutput = MfcMinOutput,
            MfcMaxOutput = MfcMaxOutput,
            PairGlassBoundary = PairGlassBoundary
        }), "관리자 설정");

        private async Task RunAsync(Func<IPlcController, Task> action, string label)
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = "PLC 미초기화"; return; }
            try
            {
                await action(plc);
                StatusMessage = $"{label} 적용됨";
            }
            catch (Exception ex)
            {
                StatusMessage = $"{label} 오류: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }
    }
}
