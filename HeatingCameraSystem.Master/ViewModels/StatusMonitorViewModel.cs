using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
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
        private readonly DispatcherTimer _timer;

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

        [ObservableProperty] private int _servoXPosition;
        [ObservableProperty] private int _servoYPosition;
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

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await PollAsync();
            _timer.Start();
        }

        private static void BuildBitItems(ObservableCollection<BitStatusItem> target, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i]))
                    target.Add(new BitStatusItem { Index = i, Name = names[i] });
        }

        private async Task PollAsync()
        {
            var plc = AppServices.PlcController;
            if (plc == null)
            {
                IsConnected = false;
                StatusMessage = "PLC 미초기화";
                return;
            }

            try
            {
                var s = await plc.ReadStatusAsync();

                CurrentTemperature = s.CurrentTemperature;
                TargetTemperature = s.TargetTemperature;
                CurrentHumidity = s.CurrentHumidity;
                TargetHumidity = s.TargetHumidity;
                BlackBody1Pv = s.BlackBody1Pv;
                BlackBody1Sv = s.BlackBody1Sv;
                BlackBody2Pv = s.BlackBody2Pv;
                BlackBody2Sv = s.BlackBody2Sv;

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

                IsConnected = true;
                StatusMessage = $"갱신 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"읽기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[StatusMonitor] {ex.Message}");
            }
        }

        private static void UpdateBits(ObservableCollection<BitStatusItem> items, bool[] bits)
        {
            foreach (var item in items)
                if (item.Index < bits.Length)
                    item.On = bits[item.Index];
        }
    }
}
