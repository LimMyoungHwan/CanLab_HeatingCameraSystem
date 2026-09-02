using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// 알람 창. 위쪽 목록에서 고른 알람의 원인·해결책을 아래에 펼치고,
    /// PLC 부저 정지·에러 리셋 같은 조치를 같은 자리에서 실행한다.
    /// 목록 비우기·삭제는 화면 목록만 건드리며 LiteDB 이력은 그대로 남는다.
    /// </summary>
    public partial class AlarmWindowViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty] private AlarmFilterOption? _selectedAlarmFilter;
        [ObservableProperty] private AlarmEntry? _selectedAlarm;
        [ObservableProperty] private string _actionMessage = string.Empty;

        public ObservableCollection<AlarmEntry> FilteredAlarms { get; } = new();
        public ObservableCollection<AlarmFilterOption> AlarmFilters { get; } = new();

        public bool HasSelection => SelectedAlarm is not null;
        public string CauseText => DetailText(true);
        public string ActionText => DetailText(false);

        public AlarmWindowViewModel()
        {
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterAll", null, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterInfo", AlarmSeverity.Info, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterWarning", AlarmSeverity.Warning, string.Empty));
            AlarmFilters.Add(new AlarmFilterOption("Nav_AlarmsFilterError", AlarmSeverity.Error, string.Empty));
            SelectedAlarmFilter = AlarmFilters[0];

            UpdateFilterLabels();
            AlarmSink.Entries.CollectionChanged += OnEntriesChanged;
            LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
            RefreshFilteredAlarms();
        }

        public void Dispose()
        {
            AlarmSink.Entries.CollectionChanged -= OnEntriesChanged;
            LocalizationManager.Instance.PropertyChanged -= OnLocalizationChanged;
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilteredAlarms();

        private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateFilterLabels();
            RaiseDetailChanged();
        }

        private void UpdateFilterLabels()
        {
            foreach (var filter in AlarmFilters)
                filter.UpdateLabel(LocalizationManager.Instance[filter.Key]);
        }

        private void RefreshFilteredAlarms()
        {
            AlarmEntry? kept = SelectedAlarm;

            FilteredAlarms.Clear();
            foreach (var alarm in AlarmSink.Entries)
            {
                if (SelectedAlarmFilter?.Severity is AlarmSeverity severity && alarm.Severity != severity)
                    continue;
                FilteredAlarms.Add(alarm);
            }

            SelectedAlarm = kept is not null && FilteredAlarms.Contains(kept) ? kept : null;
        }

        partial void OnSelectedAlarmFilterChanged(AlarmFilterOption? value) => RefreshFilteredAlarms();

        partial void OnSelectedAlarmChanged(AlarmEntry? value) => RaiseDetailChanged();

        private void RaiseDetailChanged()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CauseText));
            OnPropertyChanged(nameof(ActionText));
        }

        /// <summary>코드가 없거나 번역 키가 등록되지 않은 알람이면 안내 문구로 대체한다.</summary>
        private string DetailText(bool cause)
        {
            if (SelectedAlarm is null) return LocalizationManager.Instance["Alarm_NoSelection"];
            if (string.IsNullOrEmpty(SelectedAlarm.Code)) return LocalizationManager.Instance["Alarm_NoDetail"];

            string key = cause ? AlarmCodes.CauseKey(SelectedAlarm.Code) : AlarmCodes.ActionKey(SelectedAlarm.Code);
            return LocalizationManager.Instance.GetOrDefault(key, LocalizationManager.Instance["Alarm_NoDetail"]);
        }

        [RelayCommand]
        private Task BuzzerOff() => TriggerAsync(p => p.BuzzerOffAsync(), LocalizationManager.Instance["Alarm_BtnBuzzerOff"]);

        [RelayCommand]
        private Task ResetError() => TriggerAsync(p => p.ResetErrorAsync(), LocalizationManager.Instance["Alarm_BtnResetError"]);

        [RelayCommand]
        private void ClearAlarms() => AlarmSink.Clear();

        [RelayCommand]
        private void DeleteSelected() => AlarmSink.Remove(SelectedAlarm);

        private async Task TriggerAsync(Func<IPlcController, Task> action, string label)
        {
            IPlcController? plc = AppServices.PlcController;
            if (plc is null)
            {
                ActionMessage = LocalizationManager.Instance["Plc_NotInitialized"];
                return;
            }

            try
            {
                await action(plc);
                ActionMessage = string.Format(
                    LocalizationManager.Instance["Plc_ActionCompleted"], label, DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                ActionMessage = string.Format(
                    LocalizationManager.Instance["Plc_ActionFailed"], label, ex.Message);
            }
        }
    }
}
