using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class PointCoordRow : ObservableObject
    {
        public int Index { get; init; }

        [ObservableProperty] private float _x;
        [ObservableProperty] private float _y;
    }

    public partial class PlcControlSettingsViewModel : ObservableObject
    {
        // 온/습도 제어 · 온도 램프 · 모터/팬은 수동 조작(ManualControlViewModel)으로 이동.
        [ObservableProperty] private float _blackBody1Target = 25f;
        [ObservableProperty] private float _blackBody2Target = 25f;

        [ObservableProperty] private string _plcIpAddress = "192.168.1.2";
        [ObservableProperty] private int _plcPort = 2004;
        [ObservableProperty] private int _plcStationNo;
        [ObservableProperty] private bool _blackBodyEnabled;

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
        public Array BlackBodyConnectionTypes { get; } = Enum.GetValues<BlackBodyConnectionType>();
        public BlackBodyUnitSettings BlackBody1 { get; }
        public BlackBodyUnitSettings BlackBody2 { get; }

        public PlcControlSettingsViewModel()
        {
            for (int i = 1; i <= 20; i++)
                Points.Add(new PointCoordRow { Index = i });

            var plc = AppServices.Settings.Plc;
            _plcIpAddress = plc.IpAddress;
            _plcPort = plc.Port;
            _plcStationNo = plc.StationNo;

            var blackBody = AppServices.Settings.BlackBody;
            while (blackBody.Units.Count < 2) blackBody.Units.Add(new BlackBodyUnitSettings());
            _blackBodyEnabled = blackBody.Enabled;
            BlackBody1 = blackBody.Units[0];
            BlackBody2 = blackBody.Units[1];
        }

        [RelayCommand]
        private void SavePlcConnection()
        {
            var plc = AppServices.Settings.Plc;
            plc.IpAddress = (PlcIpAddress ?? string.Empty).Trim();
            plc.Port = PlcPort;
            plc.StationNo = PlcStationNo;
            AppServices.SaveHardwareSettings();
            StatusMessage = $"PLC 연결 저장됨 ({plc.IpAddress}:{plc.Port}) — 재시작 후 적용";
        }

        [RelayCommand]
        private void SaveBlackBodyConnection()
        {
            AppServices.Settings.BlackBody.Enabled = BlackBodyEnabled;
            AppServices.SaveHardwareSettings();
            StatusMessage = "흑체 연결 설정 저장됨 — 재시작 후 적용";
        }

        [RelayCommand]
        private Task ApplyBlackBody1() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(0, BlackBody1Target), "흑체1 온도");

        [RelayCommand]
        private Task ApplyBlackBody2() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(1, BlackBody2Target), "흑체2 온도");

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
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }
    }
}
