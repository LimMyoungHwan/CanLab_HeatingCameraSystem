using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class BitStatusItem : ObservableObject
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;

        [ObservableProperty]
        private bool _on;
    }

    public partial class StatusMonitorViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _statusMessage = "폴링 대기";

        [ObservableProperty] private float _currentTemperature;
        [ObservableProperty] private float _targetTemperature;
        [ObservableProperty] private float _currentHumidity;
        [ObservableProperty] private float _targetHumidity;
        [ObservableProperty] private float _blackBody1Pv;
        [ObservableProperty] private float _blackBody1Sv;
        [ObservableProperty] private float _blackBody2Pv;
        [ObservableProperty] private float _blackBody2Sv;

        [ObservableProperty] private float _servoXPosition;
        [ObservableProperty] private float _servoYPosition;
        [ObservableProperty] private bool _servoXBusy;
        [ObservableProperty] private bool _servoYBusy;
        [ObservableProperty] private bool _servoXHomeComplete;
        [ObservableProperty] private bool _servoYHomeComplete;
        [ObservableProperty] private int _servoXErrorCode;
        [ObservableProperty] private int _servoYErrorCode;
        [ObservableProperty] private int _currentPoint;

        [ObservableProperty] private int _currentStep;
        [ObservableProperty] private int _totalSteps;
        [ObservableProperty] private float _fanSpeedHz;
        [ObservableProperty] private float _gasFlow;

        [ObservableProperty] private bool _heater;
        [ObservableProperty] private bool _cooler1st;
        [ObservableProperty] private bool _cooler2nd;
        [ObservableProperty] private bool _coolerRoom;
        [ObservableProperty] private bool _coolerRoomBypass;
        [ObservableProperty] private bool _doorLamp;
        [ObservableProperty] private bool _pairGlass;
        [ObservableProperty] private bool _mcf;
        [ObservableProperty] private bool _blower1;
        [ObservableProperty] private bool _blower2;

        public ObservableCollection<BitStatusItem> Errors { get; } = new();
        public ObservableCollection<BitStatusItem> Inputs { get; } = new();
        public ObservableCollection<BitStatusItem> Outputs { get; } = new();

        public StatusMonitorViewModel()
        {
            BuildBitItems(Errors, PlcDeviceCatalog.ErrorNames);
            BuildBitItems(Inputs, PlcDeviceCatalog.InputNames);
            BuildBitItems(Outputs, PlcDeviceCatalog.OutputNames);

            // 공용 PlcStatusService 스냅샷을 구독한다. 자체 폴링을 돌리면 PlcXgtClient의 단일 IO
            // 세마포어를 두 배로 점유해(스냅샷 1회 = 태그 70여 회 왕복) 양쪽 모두 굶어 화면이 멈춘다.
            if (AppServices.PlcStatus == null)
            {
                StatusMessage = "PLC 미초기화";
                return;
            }
            AppServices.PlcStatus.Updated += OnSharedPlcStatus;
            Apply(AppServices.PlcStatus.Snapshot);
        }

        private static void BuildBitItems(ObservableCollection<BitStatusItem> target, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i]))
                    target.Add(new BitStatusItem { Index = i, Name = names[i] });
        }

        private async void OnSharedPlcStatus(object? sender, PlcStatusSnapshot s)
        {
            var st = AppServices.PlcStatus;
            IsConnected = st?.IsConnected ?? false;
            StatusMessage = st?.StatusMessage ?? StatusMessage;
            if (st == null || !st.IsConnected) return;

            Apply(s);
            await RefreshBlackBodyAsync(s);
        }

        private async Task RefreshBlackBodyAsync(PlcStatusSnapshot s)
        {
            try
            {
                var bb = AppServices.BlackBodyController;
                if (bb == null)
                {
                    BlackBody1Pv = s.BlackBody1Pv;
                    BlackBody1Sv = s.BlackBody1Sv;
                    BlackBody2Pv = s.BlackBody2Pv;
                    BlackBody2Sv = s.BlackBody2Sv;
                    return;
                }

                BlackBody1Pv = await bb.GetCurrentTemperatureAsync(0);
                BlackBody1Sv = await bb.GetTargetTemperatureAsync(0);
                BlackBody2Pv = await bb.GetCurrentTemperatureAsync(1);
                BlackBody2Sv = await bb.GetTargetTemperatureAsync(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StatusMonitor] black-body read failed: {ex.Message}");
            }
        }

        private void Apply(PlcStatusSnapshot s)
        {
            CurrentTemperature = s.CurrentTemperature;
            TargetTemperature = s.TargetTemperature;
            CurrentHumidity = s.CurrentHumidity;
            TargetHumidity = s.TargetHumidity;

            ServoXPosition = s.ServoXPosition;
            ServoYPosition = s.ServoYPosition;
            ServoXBusy = s.ServoXBusy;
            ServoYBusy = s.ServoYBusy;
            ServoXHomeComplete = s.ServoXHomeComplete;
            ServoYHomeComplete = s.ServoYHomeComplete;
            ServoXErrorCode = s.ServoXErrorCode;
            ServoYErrorCode = s.ServoYErrorCode;
            CurrentPoint = s.CurrentPoint;

            CurrentStep = s.CurrentStep;
            TotalSteps = s.TotalSteps;
            FanSpeedHz = s.FanSpeedHz;
            GasFlow = s.GasFlow;

            Heater = s.Heater;
            Cooler1st = s.Cooler1st;
            Cooler2nd = s.Cooler2nd;
            CoolerRoom = s.CoolerRoom;
            CoolerRoomBypass = s.CoolerRoomBypass;
            DoorLamp = s.DoorLamp;
            PairGlass = s.PairGlass;
            Mcf = s.Mcf;
            Blower1 = s.Blower1;
            Blower2 = s.Blower2;

            UpdateBits(Errors, s.ErrorBits);
            UpdateBits(Inputs, s.InputBits);
            UpdateBits(Outputs, s.OutputBits);
        }

        private static void UpdateBits(ObservableCollection<BitStatusItem> items, bool[] bits)
        {
            foreach (var item in items)
                if (item.Index < bits.Length)
                    item.On = bits[item.Index];
        }
    }
}
