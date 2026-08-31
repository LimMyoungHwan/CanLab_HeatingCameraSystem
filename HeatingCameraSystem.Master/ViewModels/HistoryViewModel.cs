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
using HeatingCameraSystem.Master.Localization;
using HeatingCameraSystem.Master.Services;
using Microsoft.Win32;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>이력 화면 촬영 이력 목록의 한 행.</summary>
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
        private double? _cameraTemperature;

        [ObservableProperty]
        private string _thumbnailUrl = string.Empty;
    }

    /// <summary>이력 화면 챔버(온·습도·흑체) 이력 목록의 한 행.</summary>
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

    /// <summary>이력 화면 알람 이력 목록의 한 행. 심각도는 번역된 표시 문자열로 담는다.</summary>
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

    /// <summary>
    /// 이력 화면. 촬영·챔버·알람 세 모드의 이력을 페이지 단위로 조회하고 CSV로 내보낸다.
    /// 날짜 범위는 <see cref="HistoryQuery.NormalizeRange"/>로 하루 전체를 포함하도록 정규화한다
    /// (WPF DatePicker가 시각을 잘라 버리기 때문).
    /// </summary>
    public partial class HistoryViewModel : ObservableObject
    {
        // 번역된 필터 라벨은 생성 시점에 캡처한다(이 VM은 이력 화면 진입 때마다 새로 만들어지므로
        // 항상 현재 언어를 반영한다). 촬영 구분/심각도 switch는 언어와 무관하게 고정인 목록
        // 인덱스로 매칭하므로 라벨을 번역해도 필터 로직은 깨지지 않는다.
        public static string AllCamerasFilter => LocalizationManager.Instance["Nav_AlarmsFilterAll"];

        [ObservableProperty]
        private DateTime _fromDateTime;

        [ObservableProperty]
        private DateTime _toDateTime;

        [ObservableProperty]
        private string _selectedCameraGroup = LocalizationManager.Instance["Nav_AlarmsFilterAll"];

        public ObservableCollection<string> CameraGroups { get; } = new ObservableCollection<string> { LocalizationManager.Instance["Nav_AlarmsFilterAll"] };

        [ObservableProperty]
        private string _selectedSourceFilter = LocalizationManager.Instance["Nav_AlarmsFilterAll"];

        public ObservableCollection<string> SourceOptions { get; } = new ObservableCollection<string>
        {
            LocalizationManager.Instance["Nav_AlarmsFilterAll"],
            LocalizationManager.Instance["Hist_SourceRecipe"],
            LocalizationManager.Instance["Hist_SourceManual"],
            LocalizationManager.Instance["Hist_SourceAgentUi"]
        };

        // 페이지네이션 속성
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

        // 선택된 로그와 상세 모달 상태
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsModalOpen))]
        private HistoryLogItem? _selectedLog;

        public bool IsModalOpen => SelectedLog != null;

        // 하단 상태 표시줄
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
            LocalizationManager.Instance["Nav_AlarmsFilterAll"],
            LocalizationManager.Instance["Hist_SevInfoAbove"],
            LocalizationManager.Instance["Hist_SevWarnAbove"],
            LocalizationManager.Instance["Hist_SevErrorOnly"]
        };

        [ObservableProperty]
        private string _selectedMinimumSeverity = LocalizationManager.Instance["Nav_AlarmsFilterAll"];

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

        /// <summary>현재 모드(촬영/챔버/알람)에 맞는 페이지 로더로 분기해 목록을 다시 채운다.</summary>
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

            List<CaptureHistoryRecord> allRecords;
            try
            {
                var (from, to) = HistoryQuery.NormalizeRange(FromDateTime, ToDateTime);
                allRecords = AppServices.HistoryRepo
                    .QueryAsync(from, to, null, 1, int.MaxValue)
                    .GetAwaiter().GetResult()
                    .ToList();
            }
            catch (Exception ex)
            {
                // 이력 화면을 여는 순간의 DB 읽기 실패가 운영자 앱을 죽여서는 안 된다.
                System.Diagnostics.Debug.WriteLine($"[History] capture query failed: {ex.Message}");
                TotalRecords = 0;
                TotalPages = 1;
                SystemStatusText = string.Format(LocalizationManager.Instance["Dash_ReadFailed"], ex.Message);
                return;
            }

            RefreshCameraFilterOptions(allRecords);

            string? cameraId = SelectedCameraGroup != AllCamerasFilter ? SelectedCameraGroup : null;
            CaptureSource? sourceFilter = HistoryQuery.SourceForIndex(SourceOptions.IndexOf(SelectedSourceFilter));
            allRecords = HistoryQuery.ApplyFilters(allRecords, cameraId, sourceFilter).ToList();

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
                    CameraTemperature = r.CameraTemperature,
                    ThumbnailUrl = r.ImagePath
                });
            }
        }

        /// <summary>챔버 이력(온·습도·흑체) 페이지를 조회해 채운다.</summary>
        private void LoadChamberPage()
        {
            ChamberItems.Clear();

            var (from, to) = HistoryQuery.NormalizeRange(FromDateTime, ToDateTime);
            var allRecords = AppServices.ChamberHistoryRepo
                .QueryAsync(from, to, 1, int.MaxValue)
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

        /// <summary>알람 이력 페이지를 조회해 채운다. 최소 심각도 필터는 콤보 인덱스로 매핑한다.</summary>
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

            AlarmSeverity? minimumSeverity = SeverityOptions.IndexOf(SelectedMinimumSeverity) switch
            {
                1 => AlarmSeverity.Info,
                2 => AlarmSeverity.Warning,
                3 => AlarmSeverity.Error,
                _ => null
            };

            var (from, to) = HistoryQuery.NormalizeRange(FromDateTime, ToDateTime);
            var allRecords = repository
                .QueryAsync(from, to, minimumSeverity, 1, int.MaxValue)
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
                        AlarmSeverity.Error => LocalizationManager.Instance["Nav_AlarmsFilterError"],
                        AlarmSeverity.Warning => LocalizationManager.Instance["Nav_AlarmsFilterWarning"],
                        _ => LocalizationManager.Instance["Nav_AlarmsFilterInfo"]
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

        /// <summary>필터·날짜 조건으로 1페이지부터 다시 조회한다.</summary>
        [RelayCommand]
        private void Search()
        {
            CurrentPage = 1;
            LoadPage();
        }

        /// <summary>촬영 이력 모드로 전환한다.</summary>
        [RelayCommand]
        private void ShowCaptureMode()
        {
            if (IsCaptureMode) return;
            IsChamberMode = false;
            IsAlarmMode = false;
            CurrentPage = 1;
            LoadPage();
        }

        /// <summary>챔버 이력 모드로 전환한다.</summary>
        [RelayCommand]
        private void ShowChamberMode()
        {
            if (IsChamberMode) return;
            IsAlarmMode = false;
            IsChamberMode = true;
            CurrentPage = 1;
            LoadPage();
        }

        /// <summary>알람 이력 모드로 전환한다.</summary>
        [RelayCommand]
        private void ShowAlarmMode()
        {
            if (IsAlarmMode) return;
            IsChamberMode = false;
            IsAlarmMode = true;
            CurrentPage = 1;
            LoadPage();
        }

        /// <summary>촬영 이력 상세 모달을 연다.</summary>
        [RelayCommand]
        private void OpenDetail(HistoryLogItem item)
        {
            SelectedLog = item;
        }

        /// <summary>상세 모달을 닫는다.</summary>
        [RelayCommand]
        private void CloseDetail()
        {
            SelectedLog = null;
        }

        /// <summary>
        /// 페이지 이동. <paramref name="direction"/>은 "first"/"prev"/"next"/"last" 또는
        /// 페이지 번호 문자열을 받는다. 범위를 벗어난 번호는 무시한다.
        /// </summary>
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

        /// <summary>
        /// 현재 필터 조건의 촬영 이력 전체(페이지 무관)를 CSV로 내보낸다.
        /// Excel 호환을 위해 UTF-8 BOM으로 저장한다.
        /// </summary>
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

            var (from, to) = HistoryQuery.NormalizeRange(FromDateTime, ToDateTime);
            string? cameraId = SelectedCameraGroup != AllCamerasFilter ? SelectedCameraGroup : null;
            CaptureSource? sourceFilter = HistoryQuery.SourceForIndex(SourceOptions.IndexOf(SelectedSourceFilter));
            var records = HistoryQuery.ApplyFilters(
                AppServices.HistoryRepo
                    .QueryAsync(from, to, null, 1, int.MaxValue)
                    .GetAwaiter().GetResult(),
                cameraId, sourceFilter).ToList();

            using var writer = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
            writer.WriteLine("Timestamp,CameraId,CameraTemperature,Temperature,Humidity,RecipeStepId,ImagePath");
            foreach (var r in records)
            {
                writer.WriteLine(string.Join(',',
                    r.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                    CsvEscape(r.CameraId),
                    r.CameraTemperature?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty,
                    r.Temperature.ToString("F2", CultureInfo.InvariantCulture),
                    r.Humidity.ToString("F2", CultureInfo.InvariantCulture),
                    CsvEscape(r.RecipeStepId),
                    CsvEscape(r.ImagePath)));
            }

            SystemStatusText = $"Exported {records.Count} records to {Path.GetFileName(dlg.FileName)}";
        }

        /// <summary>쉼표·따옴표·개행이 포함된 값을 CSV 규칙(따옴표 감싸기 + 이중 따옴표)으로 이스케이프한다.</summary>
        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
