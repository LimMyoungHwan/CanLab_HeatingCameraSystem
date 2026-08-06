using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Microsoft.Win32;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class HistoryLogItem : ObservableObject
    {
        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private string _cameraId = string.Empty;

        [ObservableProperty]
        private float _temperature;

        [ObservableProperty]
        private float _humidity;

        [ObservableProperty]
        private string _thumbnailUrl = string.Empty;
    }

    public partial class ChamberHistoryLogItem : ObservableObject
    {
        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private float _temperature;

        [ObservableProperty]
        private float _humidity;

        [ObservableProperty]
        private float _blackBody1;

        [ObservableProperty]
        private float _blackBody2;
    }

    public partial class AlarmHistoryLogItem : ObservableObject
    {
        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private string _severity = string.Empty;

        [ObservableProperty]
        private string _source = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;
    }

    public partial class HistoryViewModel : ObservableObject
    {
        // Filter properties
        public const string AllCamerasFilter = "전체";

        [ObservableProperty]
        private DateTime _fromDateTime;

        [ObservableProperty]
        private DateTime _toDateTime;

        [ObservableProperty]
        private string _selectedCameraGroup = AllCamerasFilter;

        public ObservableCollection<string> CameraGroups { get; } = new ObservableCollection<string> { AllCamerasFilter };

        public const string AllSourcesFilter = "전체";

        [ObservableProperty]
        private string _selectedSourceFilter = AllSourcesFilter;

        public ObservableCollection<string> SourceOptions { get; } = new ObservableCollection<string>
        {
            AllSourcesFilter, "레시피", "수동", "AgentUI"
        };

        // Pagination properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _currentPage = 1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _totalPages = 1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _totalRecords;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _pageSize = 10;

        public string ShowingRecordsText
        {
            get
            {
                int start = (CurrentPage - 1) * PageSize + 1;
                int end = Math.Min(CurrentPage * PageSize, TotalRecords);
                return $"Showing {start}-{end} of {TotalRecords:N0} records";
            }
        }

        // Selected log and modal state
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModalOpen))]
        private HistoryLogItem? _selectedLog;

        public bool IsModalOpen => SelectedLog != null;

        // Stats Footer
        [ObservableProperty]
        private string _systemStatusText = "System Status: Nominal";

        [ObservableProperty]
        private string _dbLatencyText = "DB Latency: 42ms";

        [ObservableProperty]
        private string _versionText = "V2.4.1 Build 9022";

        public ObservableCollection<HistoryLogItem> LogItems { get; } = new ObservableCollection<HistoryLogItem>();

        public ObservableCollection<ChamberHistoryLogItem> ChamberItems { get; } = new ObservableCollection<ChamberHistoryLogItem>();

        public ObservableCollection<AlarmHistoryLogItem> AlarmItems { get; } = new ObservableCollection<AlarmHistoryLogItem>();

        public ObservableCollection<string> SeverityOptions { get; } = new ObservableCollection<string>
        {
            "전체",
            "정보 이상",
            "경고 이상",
            "오류만"
        };

        [ObservableProperty]
        private string _selectedMinimumSeverity = "전체";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCaptureMode))]
        private bool _isChamberMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCaptureMode))]
        private bool _isAlarmMode;

        public bool IsCaptureMode => !IsChamberMode && !IsAlarmMode;

        public HistoryViewModel()
        {
            FromDateTime = DateTime.Today.AddDays(-2);
            ToDateTime = DateTime.Today.AddDays(1).AddSeconds(-1);
            LoadPage();
        }

        private void LoadPage()
        {
            if (IsAlarmMode)
            {
                LoadAlarmPage();
                return;
            }

            if (IsChamberMode)
            {
                LoadChamberPage();
                return;
            }

            LogItems.Clear();

            var allRecords = AppServices.HistoryRepo
                .QueryAsync(FromDateTime, ToDateTime, null, 1, int.MaxValue)
                .GetAwaiter().GetResult()
                .ToList();

            RefreshCameraFilterOptions(allRecords);

            if (SelectedCameraGroup != AllCamerasFilter)
                allRecords = allRecords.Where(r => r.CameraId == SelectedCameraGroup).ToList();

            CaptureSource? sourceFilter = SelectedSourceFilter switch
            {
                "레시피" => CaptureSource.Recipe,
                "수동" => CaptureSource.Manual,
                "AgentUI" => CaptureSource.AgentUi,
                _ => null
            };
            if (sourceFilter.HasValue)
                allRecords = allRecords.Where(r => r.Source == sourceFilter.Value).ToList();

            TotalRecords = allRecords.Count;
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
            if (TotalPages == 0) TotalPages = 1;
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            foreach (var r in allRecords.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            {
                LogItems.Add(new HistoryLogItem
                {
                    Timestamp = r.Timestamp,
                    CameraId = r.CameraId,
                    Temperature = r.Temperature,
                    Humidity = r.Humidity,
                    ThumbnailUrl = r.ImagePath
                });
            }
        }

        private void LoadChamberPage()
        {
            ChamberItems.Clear();

            var allRecords = AppServices.ChamberHistoryRepo
                .QueryAsync(FromDateTime, ToDateTime, 1, int.MaxValue)
                .GetAwaiter().GetResult()
                .ToList();

            TotalRecords = allRecords.Count;
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
            if (TotalPages == 0) TotalPages = 1;
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            foreach (var r in allRecords.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            {
                ChamberItems.Add(new ChamberHistoryLogItem
                {
                    Timestamp = r.Timestamp,
                    Temperature = r.Temperature,
                    Humidity = r.Humidity,
                    BlackBody1 = r.BlackBody1,
                    BlackBody2 = r.BlackBody2
                });
            }
        }

        private void LoadAlarmPage()
        {
            AlarmItems.Clear();
            var repository = AppServices.AlarmHistoryRepo;
            if (repository == null)
            {
                TotalRecords = 0;
                TotalPages = 1;
                return;
            }

            AlarmSeverity? minimumSeverity = SelectedMinimumSeverity switch
            {
                "정보 이상" => AlarmSeverity.Info,
                "경고 이상" => AlarmSeverity.Warning,
                "오류만" => AlarmSeverity.Error,
                _ => null
            };

            var allRecords = repository
                .QueryAsync(FromDateTime, ToDateTime, minimumSeverity, 1, int.MaxValue)
                .GetAwaiter().GetResult()
                .ToList();

            TotalRecords = allRecords.Count;
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
            if (TotalPages == 0) TotalPages = 1;
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            foreach (var record in allRecords.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
            {
                AlarmItems.Add(new AlarmHistoryLogItem
                {
                    Timestamp = record.Timestamp,
                    Severity = record.Severity switch
                    {
                        AlarmSeverity.Error => "오류",
                        AlarmSeverity.Warning => "경고",
                        _ => "정보"
                    },
                    Source = record.Source,
                    Message = record.Message
                });
            }
        }

        // 집합이 바뀔 때만 갱신 — ObservableCollection 재구축 시 바인딩된 ComboBox 선택 리셋 방지.
        private void RefreshCameraFilterOptions(IEnumerable<CaptureHistoryRecord> records)
        {
            var desired = new List<string> { AllCamerasFilter };
            desired.AddRange(records
                .Select(r => r.CameraId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase));

            if (CameraGroups.SequenceEqual(desired)) return;

            string previous = SelectedCameraGroup;
            CameraGroups.Clear();
            foreach (string id in desired) CameraGroups.Add(id);
            SelectedCameraGroup = desired.Contains(previous) ? previous : AllCamerasFilter;
        }

        [RelayCommand]
        private void Search()
        {
            CurrentPage = 1;
            LoadPage();
        }

        [RelayCommand]
        private void ShowCaptureMode()
        {
            if (IsCaptureMode) return;
            IsChamberMode = false;
            IsAlarmMode = false;
            CurrentPage = 1;
            LoadPage();
        }

        [RelayCommand]
        private void ShowChamberMode()
        {
            if (IsChamberMode) return;
            IsAlarmMode = false;
            IsChamberMode = true;
            CurrentPage = 1;
            LoadPage();
        }

        [RelayCommand]
        private void ShowAlarmMode()
        {
            if (IsAlarmMode) return;
            IsChamberMode = false;
            IsAlarmMode = true;
            CurrentPage = 1;
            LoadPage();
        }

        [RelayCommand]
        private void OpenDetail(HistoryLogItem item)
        {
            SelectedLog = item;
        }

        [RelayCommand]
        private void CloseDetail()
        {
            SelectedLog = null;
        }

        [RelayCommand]
        private void MovePage(string direction)
        {
            switch (direction.ToLower())
            {
                case "first":
                    CurrentPage = 1;
                    break;
                case "prev":
                    if (CurrentPage > 1) CurrentPage--;
                    break;
                case "next":
                    if (CurrentPage < TotalPages) CurrentPage++;
                    break;
                case "last":
                    CurrentPage = TotalPages;
                    break;
                default:
                    if (int.TryParse(direction, out int pageNum))
                    {
                        if (pageNum >= 1 && pageNum <= TotalPages)
                            CurrentPage = pageNum;
                    }
                    break;
            }
            LoadPage();
        }

        [RelayCommand]
        private void ExportCsv()
        {
            var dlg = new SaveFileDialog
            {
                Title    = "Export history to CSV",
                Filter   = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"history_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var records = AppServices.HistoryRepo
                .QueryAsync(FromDateTime, ToDateTime, null, 1, int.MaxValue)
                .GetAwaiter().GetResult()
                .ToList();

            if (SelectedCameraGroup != AllCamerasFilter)
                records = records.Where(r => r.CameraId == SelectedCameraGroup).ToList();

            using var writer = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
            writer.WriteLine("Timestamp,CameraId,Temperature,Humidity,RecipeStepId,ImagePath");
            foreach (var r in records)
            {
                writer.WriteLine(string.Join(',',
                    r.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                    CsvEscape(r.CameraId),
                    r.Temperature.ToString("F2", CultureInfo.InvariantCulture),
                    r.Humidity.ToString("F2", CultureInfo.InvariantCulture),
                    CsvEscape(r.RecipeStepId),
                    CsvEscape(r.ImagePath)));
            }

            SystemStatusText = $"Exported {records.Count} records to {Path.GetFileName(dlg.FileName)}";
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
