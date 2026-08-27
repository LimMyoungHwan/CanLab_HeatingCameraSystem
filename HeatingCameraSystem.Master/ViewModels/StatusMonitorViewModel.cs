using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>PLC 비트(에러/입력/출력) 1개의 표시 항목. Name은 번역된 라벨이다.</summary>
    public partial class BitStatusItem : ObservableObject
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;

        [ObservableProperty]
        private bool _on;
    }

    /// <summary>
    /// PLC 상태 모니터 화면. 온·습도, 서보, 장비, 에러/입력/출력 비트 전체를 표시한다.
    /// PLC를 직접 폴링하지 않고 공용 PlcStatusService 이벤트만 구독한다.
    /// MainViewModel이 이 VM을 캐시해 두므로 구독은 앱 수명 동안 1회다.
    /// </summary>
    public partial class StatusMonitorViewModel : ObservableObject
    {
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _statusMessage = LocalizationManager.Instance["Status_PollingWait"];

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
            BuildBitItems(Errors, PlcDeviceCatalog.ErrorNames, "PlcErr_");
            BuildBitItems(Inputs, PlcDeviceCatalog.InputNames, "PlcIn_");
            BuildBitItems(Outputs, PlcDeviceCatalog.OutputNames, "PlcOut_");

            LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

            // 공용 PlcStatusService 스냅샷을 구독한다. 자체 폴링을 돌리면 PlcXgtClient의 단일 IO
            // 세마포어를 두 배로 점유해(스냅샷 1회 = 태그 70여 회 왕복) 양쪽 모두 굶어 화면이 멈춘다.
            if (AppServices.PlcStatus == null)
            {
                StatusMessage = LocalizationManager.Instance["Plc_NotInitialized"];
                return;
            }
            AppServices.PlcStatus.Updated += OnSharedPlcStatus;
            AppServices.PlcStatus.BlackBodyUpdated += OnBlackBodyUpdated;
            Apply(AppServices.PlcStatus.Snapshot);
        }

        /// <summary>비트 카탈로그에서 이름이 있는 항목만 번역된 라벨로 목록을 재구성한다(언어 전환 시 재호출).</summary>
        private static void BuildBitItems(ObservableCollection<BitStatusItem> target, string[] names, string keyPrefix)
        {
            target.Clear();
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i]))
                    target.Add(new BitStatusItem { Index = i, Name = LocalizationManager.Instance.GetOrDefault(keyPrefix + i, names[i]) });
        }

        private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            BuildBitItems(Errors, PlcDeviceCatalog.ErrorNames, "PlcErr_");
            BuildBitItems(Inputs, PlcDeviceCatalog.InputNames, "PlcIn_");
            BuildBitItems(Outputs, PlcDeviceCatalog.OutputNames, "PlcOut_");
            if (AppServices.PlcStatus != null) Apply(AppServices.PlcStatus.Snapshot);
        }

        /// <summary>공용 스냅샷 갱신 이벤트 수신. 연결이 끊기면 PLC 값을 기본값으로 비우되 흑체 값은 유지한다.</summary>
        private void OnSharedPlcStatus(object? sender, PlcStatusSnapshot s)
        {
            var st = AppServices.PlcStatus;
            IsConnected = st?.IsConnected ?? false;
            StatusMessage = st?.StatusMessage ?? StatusMessage;
            if (st == null || !st.IsConnected)
            {
                // PLC 값만 비운다. 흑체는 독립 폴링이므로 마지막 판독값을 계속 보여준다.
                Apply(new PlcStatusSnapshot());
                OnBlackBodyUpdated(this, EventArgs.Empty);
                return;
            }

            Apply(s);
        }

        // 흑체 값은 화면에서 재판독하지 않고 공용 서비스가 병합해 둔 값을 그대로 소비한다
        // (흑체 폴링은 PLC와 독립된 타이머라 PLC가 끊겨도 계속 갱신된다).
        private void OnBlackBodyUpdated(object? sender, EventArgs e)
        {
            var st = AppServices.PlcStatus;
            if (st == null) return;
            BlackBody1Pv = st.BlackBody1Pv;
            BlackBody1Sv = st.BlackBody1Sv;
            BlackBody2Pv = st.BlackBody2Pv;
            BlackBody2Sv = st.BlackBody2Sv;
        }

        /// <summary>스냅샷 전체를 바인딩 속성과 비트 목록에 반영한다.</summary>
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

            BlackBody1Pv = s.BlackBody1Pv;
            BlackBody1Sv = s.BlackBody1Sv;
            BlackBody2Pv = s.BlackBody2Pv;
            BlackBody2Sv = s.BlackBody2Sv;

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
