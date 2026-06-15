using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Master.Services;

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

    public partial class HistoryViewModel : ObservableObject
    {
        // Filter properties
        [ObservableProperty]
        private DateTime _fromDateTime;

        [ObservableProperty]
        private DateTime _toDateTime;

        [ObservableProperty]
        private string _selectedCameraGroup = "All Units";

        public ObservableCollection<string> CameraGroups { get; } = new ObservableCollection<string>
        {
            "All Units",
            "Agent-PC-01 (CAM-01 to CAM-16)",
            "Agent-PC-02 (CAM-17 to CAM-32)",
            "Agent-PC-03 (CAM-33 to CAM-64)"
        };

        // Pagination properties
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _currentPage = 1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _totalPages = 125;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowingRecordsText))]
        private int _totalRecords = 1248;

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

        public HistoryViewModel()
        {
            FromDateTime = DateTime.Today.AddDays(-2);
            ToDateTime = DateTime.Today.AddDays(1).AddSeconds(-1);
            LoadPage();
        }

        private void LoadPage()
        {
            LogItems.Clear();

            var allRecords = AppServices.HistoryRepo
                .QueryAsync(FromDateTime, ToDateTime, null, 1, int.MaxValue)
                .GetAwaiter().GetResult()
                .ToList();

            if (SelectedCameraGroup != "All Units")
            {
                int min = 1, max = 64;
                if (SelectedCameraGroup.Contains("Agent-PC-01")) { min = 1; max = 16; }
                else if (SelectedCameraGroup.Contains("Agent-PC-02")) { min = 17; max = 32; }
                else if (SelectedCameraGroup.Contains("Agent-PC-03")) { min = 33; max = 64; }

                allRecords = allRecords.Where(r =>
                {
                    if (int.TryParse(r.CameraId.Replace("CAM-", ""), out int n))
                        return n >= min && n <= max;
                    return false;
                }).ToList();
            }

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

        [RelayCommand]
        private void Search()
        {
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
        private void EmergencyStop()
        {
            SystemStatusText = "System Status: EMERGENCY STOPPED";
        }
    }
}
