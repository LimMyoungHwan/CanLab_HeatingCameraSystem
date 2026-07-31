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

    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentViewTitle = string.Empty;

        [ObservableProperty]
        private object? _currentViewModel;

        private readonly DashboardViewModel _dashboardViewModel = new();
        // PlcStatus.Updated 구독자이므로 매 진입마다 새로 만들면 죽은 VM이 계속 이벤트를 받는다.
        private StatusMonitorViewModel? _statusMonitorViewModel;
        private string _currentTitleKey = "Title_Dashboard";

        [ObservableProperty]
        private AlarmFilterOption? _selectedAlarmFilter;

        public ObservableCollection<AlarmEntry> Alarms => AlarmSink.Entries;
        public ObservableCollection<AlarmEntry> FilteredAlarms { get; } = new();
        public ObservableCollection<AlarmFilterOption> AlarmFilters { get; } = new();

        public PlcStatusService? PlcStatus => AppServices.PlcStatus;

        public MainViewModel()
        {
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterAll", null, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterInfo", AlarmSeverity.Info, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterWarning", AlarmSeverity.Warning, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterError", AlarmSeverity.Error, string.Empty));
            SelectedAlarmFilter = AlarmFilters[0];
            UpdateAlarmFilterLabels();
            AlarmSink.Entries.CollectionChanged += OnAlarmEntriesChanged;
            LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
            RefreshFilteredAlarms();
            NavigateToDashboard();
        }

        private void OnAlarmEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilteredAlarms();

        private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateAlarmFilterLabels();
            UpdateTitle();
        }

        private void UpdateAlarmFilterLabels()
        {
            foreach (var filter in AlarmFilters)
                filter.UpdateLabel(LocalizationManager.Instance[filter.Key]);
        }

        private void RefreshFilteredAlarms()
        {
            FilteredAlarms.Clear();
            foreach (var alarm in AlarmSink.Entries)
            {
                if (SelectedAlarmFilter?.Severity is AlarmSeverity severity && alarm.Severity != severity)
                    continue;
                FilteredAlarms.Add(alarm);
            }
        }

        partial void OnSelectedAlarmFilterChanged(AlarmFilterOption? value) => RefreshFilteredAlarms();

        [RelayCommand]
        private void DeleteAlarm(AlarmEntry? entry) => AlarmSink.Remove(entry);

        private void UpdateTitle() => CurrentViewTitle = LocalizationManager.Instance[_currentTitleKey];

        // 알람 처리용 모멘터리 트리거. PlcXgtClient가 ON → PulseHoldMs → OFF까지 처리한다.
        [ObservableProperty]
        private string _alarmActionMessage = string.Empty;

        [RelayCommand]
        private Task BuzzerOff() => TriggerAsync(p => p.BuzzerOffAsync(), "부저 OFF");

        [RelayCommand]
        private Task ResetError() => TriggerAsync(p => p.ResetErrorAsync(), "에러 리셋");

        [RelayCommand]
        private Task PlcOrigin() => TriggerAsync(async p =>
        {
            await p.SetPointCoordinateAsync(1, 0f, 0f);
            await p.MoveServoToPositionAsync(1);
        }, LocalizationManager.Instance["Plc_Origin"]);

        [RelayCommand]
        private Task EmergencyStop() => TriggerAsync(p => p.TriggerEmergencyStopAsync(), "비상정지");

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

        [RelayCommand]
        private void NavigateToDashboard()
        {
            _currentTitleKey = "Title_Dashboard";
            CurrentViewModel = _dashboardViewModel;
            _dashboardViewModel.RefreshRecipesCommand.Execute(null);
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToRecipeEditor()
        {
            _currentTitleKey = "Title_RecipeEditor";
            CurrentViewModel = new RecipeEditorViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToHistory()
        {
            _currentTitleKey = "Title_History";
            CurrentViewModel = new HistoryViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToAgentSettings()
        {
            _currentTitleKey = "Title_AgentSettings";
            CurrentViewModel = new AgentSettingsViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToStatusMonitor()
        {
            _currentTitleKey = "Title_PlcStatus";
            CurrentViewModel = _statusMonitorViewModel ??= new StatusMonitorViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToPlcControlSettings()
        {
            _currentTitleKey = "Title_PlcSettings";
            CurrentViewModel = new PlcControlSettingsViewModel();
            UpdateTitle();
        }

        [RelayCommand]
        private void NavigateToManualControl()
        {
            _currentTitleKey = "Title_ManualControl";
            CurrentViewModel = new ManualControlViewModel();
            UpdateTitle();
        }
    }
}
