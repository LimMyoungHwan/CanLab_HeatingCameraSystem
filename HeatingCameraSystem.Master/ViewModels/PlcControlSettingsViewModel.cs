using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>포인트 좌표 그리드의 한 행. 좌표 단위는 mm다.</summary>
    public partial class PointCoordRow : ObservableObject
    {
        public int Index { get; init; }

        [ObservableProperty] private float _x;
        [ObservableProperty] private float _y;
    }

    /// <summary>
    /// PLC 설정 화면. PLC/흑체 연결 정보(hardware.json), 포인트 좌표 1~20, 관리자 파라미터를
    /// 읽고 쓴다. 흑체 목표 온도 즉시 적용 기능도 이 화면에 남아 있다.
    /// </summary>
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

        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Common_Idle"];

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

        /// <summary>PLC 연결 정보(IP/포트/국번)를 hardware.json에 저장한다. 적용에는 재시작이 필요하다.</summary>
        [RelayCommand]
        private void SavePlcConnection()
        {
            var plc = AppServices.Settings.Plc;
            plc.IpAddress = (PlcIpAddress ?? string.Empty).Trim();
            plc.Port = PlcPort;
            plc.StationNo = PlcStationNo;
            AppServices.SaveHardwareSettings();
            StatusMessage = L("Plc_ConnSaved", plc.IpAddress, plc.Port);
        }

        /// <summary>흑체 연결 설정(사용 여부 포함)을 hardware.json에 저장한다. 유닛 설정은 바인딩으로 이미 반영된 상태다.</summary>
        [RelayCommand]
        private void SaveBlackBodyConnection()
        {
            AppServices.Settings.BlackBody.Enabled = BlackBodyEnabled;
            AppServices.SaveHardwareSettings();
            StatusMessage = LocalizationManager.Instance["Plc_BbConnSaved"];
        }

        /// <summary>흑체 1 목표 온도(℃)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyBlackBody1() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(0, BlackBody1Target), LocalizationManager.Instance["Plc_Bb1Temp"]);

        /// <summary>흑체 2 목표 온도(℃)를 적용한다.</summary>
        [RelayCommand]
        private Task ApplyBlackBody2() => RunBlackBodyAsync(bb => bb.SetTemperatureAsync(1, BlackBody2Target), LocalizationManager.Instance["Plc_Bb2Temp"]);

        /// <summary>PLC에서 포인트 1~20의 좌표(mm)를 읽어 그리드에 채운다.</summary>
        [RelayCommand]
        private async Task LoadPoints()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }
            try
            {
                foreach (var row in Points)
                {
                    var (x, y) = await plc.GetPointCoordinateAsync(row.Index);
                    row.X = x;
                    row.Y = y;
                }
                StatusMessage = LocalizationManager.Instance["Plc_PointsLoaded"];
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_LoadError", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        /// <summary>그리드의 포인트 1~20 좌표(mm)를 PLC에 쓴다.</summary>
        [RelayCommand]
        private async Task SavePoints()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }
            try
            {
                foreach (var row in Points)
                    await plc.SetPointCoordinateAsync(row.Index, row.X, row.Y);
                StatusMessage = LocalizationManager.Instance["Plc_PointsSaved"];
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_SaveError", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        /// <summary>PLC 상태 스냅샷에서 관리자 파라미터(과열 한계·쿨러 경계 등)를 읽어 온다.</summary>
        [RelayCommand]
        private async Task LoadAdmin()
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }
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
                StatusMessage = LocalizationManager.Instance["Plc_AdminLoaded"];
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_LoadError", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

        /// <summary>편집한 관리자 파라미터 전체를 PLC에 쓴다.</summary>
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
        }), LocalizationManager.Instance["Plc_AdminLabel"]);

        private static string L(string key, params object[] args) => string.Format(LocalizationManager.Instance[key], args);

        /// <summary>PLC 제어 공통 실행기. 미초기화·실패를 <see cref="StatusMessage"/>로 보고한다.</summary>
        private async Task RunAsync(Func<IPlcController, Task> action, string label)
        {
            var plc = AppServices.PlcController;
            if (plc == null) { StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"]; return; }
            try
            {
                await action(plc);
                StatusMessage = L("Plc_LabelApplied", label);
            }
            catch (Exception ex)
            {
                StatusMessage = L("Plc_LabelError", label, ex.Message);
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }

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
                System.Diagnostics.Debug.WriteLine($"[PlcSettings] {ex.Message}");
            }
        }
    }
}
