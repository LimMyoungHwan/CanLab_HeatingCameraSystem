using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// AgentUiLog가 남긴 .ndjson 로그 파일을 되읽어 보여주는 인앱 로그 뷰어 뷰모델.
    /// 최소 레벨을 바꾸면 즉시 다시 읽는다.
    /// </summary>
    public partial class LogViewerViewModel : ObservableObject
    {
        private readonly string _logDir;

        public ObservableCollection<LogEntry> Entries { get; } = new();

        public Array Levels { get; } = Enum.GetValues(typeof(LogEntryLevel));

        [ObservableProperty]
        private LogEntryLevel _minLevel = LogEntryLevel.Information;

        [ObservableProperty]
        private string _statusText = string.Empty;

        public LogViewerViewModel(string logDir)
        {
            _logDir = logDir;
            Refresh();
        }

        partial void OnMinLevelChanged(LogEntryLevel value) => Refresh();

        [RelayCommand]
        private void Refresh()
        {
            Entries.Clear();
            foreach (LogEntry entry in NdjsonLogReader.Read(_logDir, MinLevel, limit: 1000))
            {
                Entries.Add(entry);
            }

            StatusText = $"{Entries.Count} entries (>= {MinLevel})";
        }
    }
}
