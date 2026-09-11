using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// 알람 패널 심각도 필터 항목. Key는 번역 키, Severity가 null이면 "전체"다.
    /// 라벨은 언어 전환 시 <see cref="UpdateLabel"/>로 갈아 끼운다.
    /// </summary>
    public sealed class AlarmFilterOption : ObservableObject
    {
        private string _label;

        public AlarmFilterOption(string key, AlarmSeverity? severity, string label)
        {
            Key = key;
            Severity = severity;
            _label = label;
        }

        public string Key { get; }
        public AlarmSeverity? Severity { get; }
        public string Label
        {
            get => _label;
            private set => SetProperty(ref _label, value);
        }

        public void UpdateLabel(string label) => Label = label;
    }

    /// <summary>
    /// 메인 셸 화면. 좌측 내비게이션으로 각 화면 ViewModel을 전환하고, 상단 알람 패널
    /// (필터·삭제)과 부저 정지·에러 리셋·원점 복귀·비상정지 등 전역 PLC 트리거를 담당한다.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentViewTitle = string.Empty;

        [ObservableProperty]
        private object? _currentViewModel;

        /// <summary>시뮬레이션 배너 표시 여부. 구성은 재시작 때만 바뀌므로 세션 내내 고정이다.</summary>
        public bool IsSimulationMode => AppServices.Settings.SimulationMode;

        /// <summary>어느 장비가 가짜인지까지 보여준다. "시뮬레이션 모드"만으로는 PLC만인지 전부인지 구분이 안 된다.</summary>
        public string SimulationTargetsText => AppServices.DescribeSimulated(AppServices.Settings);

        private readonly DashboardViewModel _dashboardViewModel = new();
        // PlcStatus.Updated 구독자이므로 매 진입마다 새로 만들면 죽은 VM이 계속 이벤트를 받는다.
        private StatusMonitorViewModel? _statusMonitorViewModel;
        private string _currentTitleKey = "Title_Dashboard";

        public ObservableCollection<AlarmEntry> Alarms => AlarmSink.Entries;

        public PlcStatusService? PlcStatus => AppServices.PlcStatus;

        public MainViewModel()
        {
            LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
            NavigateToDashboard();
        }

        private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            => UpdateTitle();

        /// <summary>알람 창을 연다. 이미 떠 있으면 그 창을 앞으로 가져온다.</summary>
        [RelayCommand]
        private void OpenAlarmWindow()
        {
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is Views.AlarmWindow existing)
                {
                    existing.Activate();
                    return;
                }
            }

            new Views.AlarmWindow { Owner = System.Windows.Application.Current.MainWindow }.Show();
        }

        private void UpdateTitle() => CurrentViewTitle = LocalizationManager.Instance[_currentTitleKey];

        // 알람 처리용 모멘터리 트리거. PlcXgtClient가 ON → PulseHoldMs → OFF까지 처리한다.
        [ObservableProperty]
        private string _alarmActionMessage = string.Empty;

        /// <summary>부저 정지 트리거.</summary>
        [RelayCommand]
        private Task BuzzerOff() => TriggerAsync(p => p.BuzzerOffAsync(), LocalizationManager.Instance["Nav_BuzzerOff"]);

        /// <summary>PLC 에러 리셋 트리거.</summary>
        [RelayCommand]
        private Task ResetError() => TriggerAsync(p => p.ResetErrorAsync(), LocalizationManager.Instance["Nav_ErrorReset"]);

        /// <summary>원점 복귀: 포인트 1 좌표를 (0,0)으로 쓰고 그 포인트로 이동시킨다.</summary>
        [RelayCommand]
        private Task PlcOrigin() => TriggerAsync(async p =>
        {
            await p.SetPointCoordinateAsync(1, 0f, 0f);
            await p.MoveServoToPositionAsync(1);
        }, LocalizationManager.Instance["Plc_Origin"]);

        /// <summary>비상정지 트리거.</summary>
        [RelayCommand]
        private Task EmergencyStop()
        {
            AppServices.RecipeEngine?.RequestEmergencyStop();
            return TriggerAsync(p => p.TriggerEmergencyStopAsync(), LocalizationManager.Instance["Equip_EStop"]);
        }

        /// <summary>전역 PLC 트리거 공통 실행기. 성공/실패를 <see cref="AlarmActionMessage"/>로 보고한다.</summary>
        private async Task TriggerAsync(Func<IPlcController, Task> action, string label)
        {
            var plc = AppServices.PlcController;
            if (plc == null)
            {
                AlarmActionMessage = LocalizationManager.Instance["Plc_NotInitialized"];
                return;
            }

            try
            {
                await action(plc);
                AlarmActionMessage = string.Format(
                    LocalizationManager.Instance["Plc_ActionCompleted"], label, DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                AlarmActionMessage = string.Format(
                    LocalizationManager.Instance["Plc_ActionFailed"], label, ex.Message);
                System.Diagnostics.Debug.WriteLine($"[Main] {label} failed: {ex.Message}");
            }
        }

        /// <summary>대시보드로 전환한다. 대시보드 VM은 앱 수명 동안 1개를 재사용하며, 진입 시 레시피 목록만 새로고침한다.</summary>
        [RelayCommand]
        private void NavigateToDashboard()
        {
            _currentTitleKey = "Title_Dashboard";
            CurrentViewModel = _dashboardViewModel;
            _dashboardViewModel.RefreshRecipesCommand.Execute(null);
            UpdateTitle();
        }

        /// <summary>레시피 편집 화면으로 전환한다. 진입할 때마다 VM을 새로 만든다.</summary>
        [RelayCommand]
        private void NavigateToRecipeEditor()
        {
            _currentTitleKey = "Title_RecipeEditor";
            CurrentViewModel = new RecipeEditorViewModel();
            UpdateTitle();
        }

        /// <summary>이력 화면으로 전환한다. 진입할 때마다 VM을 새로 만들어 현재 언어의 필터 라벨을 반영한다.</summary>
        [RelayCommand]
        private void NavigateToHistory()
        {
            _currentTitleKey = "Title_History";
            CurrentViewModel = new HistoryViewModel();
            UpdateTitle();
        }

        /// <summary>레시피 결과 화면으로 전환한다. 진입할 때마다 회차 목록을 새로 읽는다.</summary>
        [RelayCommand]
        private void NavigateToRecipeResult()
        {
            _currentTitleKey = "Title_RecipeResult";
            CurrentViewModel = new RecipeResultViewModel();
            UpdateTitle();
        }

        /// <summary>Agent 설정 화면으로 전환한다.</summary>
        [RelayCommand]
        private void NavigateToAgentSettings()
        {
            _currentTitleKey = "Title_AgentSettings";
            CurrentViewModel = new AgentSettingsViewModel();
            UpdateTitle();
        }

        /// <summary>PLC 상태 모니터 화면으로 전환한다. 이벤트 구독 중복을 막기 위해 VM을 캐시해 재사용한다.</summary>
        [RelayCommand]
        private void NavigateToStatusMonitor()
        {
            _currentTitleKey = "Title_PlcStatus";
            CurrentViewModel = _statusMonitorViewModel ??= new StatusMonitorViewModel();
            UpdateTitle();
        }

        /// <summary>PLC 설정 화면으로 전환한다.</summary>
        [RelayCommand]
        private void NavigateToPlcControlSettings()
        {
            _currentTitleKey = "Title_PlcSettings";
            CurrentViewModel = new PlcControlSettingsViewModel();
            UpdateTitle();
        }

        /// <summary>수동 조작 화면으로 전환한다.</summary>
        [RelayCommand]
        private void NavigateToManualControl()
        {
            _currentTitleKey = "Title_ManualControl";
            CurrentViewModel = new ManualControlViewModel();
            UpdateTitle();
        }
    }
}
