using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        private string _thumbnailUrl = string.Empty; // In real app, local path or URL
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
        private List<HistoryLogItem> _allMockLogs = new List<HistoryLogItem>();

        public HistoryViewModel()
        {
            // Default Filter dates
            FromDateTime = DateTime.Today.AddDays(-2);
            ToDateTime = DateTime.Today.AddDays(1).AddSeconds(-1);

            GenerateMockData();
            LoadPage();
        }

        private void GenerateMockData()
        {
            var random = new Random(100);
            var now = DateTime.Now;

            // Generate 1248 logs
            for (int i = 0; i < 1248; i++)
            {
                int camNum = random.Next(1, 65);
                float temp = 25.0f + (float)random.NextDouble() * 25.0f; // 25 to 50
                float humidity = 35.0f + (float)random.NextDouble() * 15.0f; // 35 to 50

                // HTML Mockup uses 2 static test image urls
                string imgUrl = camNum % 2 == 0 
                    ? "https://lh3.googleusercontent.com/aida/AP1WRLsqVbcuiVRcKBpaI85kWS4D5Me5-Cvd77EZzkSjNNeb0GuMwUSA0dmAbXUo6w8YQIbEyJgD6oAPSvn-vDnKB2Uq1fUCvBJQwYZYZjERKwvuYTKqThqBVVmVxro41fwZy3Iu1k64vBm9eqyjCicV8CcP_uyhTNsO35JlbSRSbAxPGPimBLxD5kwIYFr27MNVJ6AD0wsA_GQiqW748dRKodERWh8cpVlWvK-JT19iqLiW_lNlDRVxc97znRsS"
                    : "https://lh3.googleusercontent.com/aida/AP1WRLvOi1m9ukqQco9BgJcjhlTTmqfHz0vo-NmooxNMh9sv1GkTX9ZUdzIZZPxeI4r9PSYyDdznya_WUkz0HTgHZLtKMJZU-mHzJtF3A8SpLl_Gry0FLDntzN4556t9WA54RF000N7JYQeeAL7zhPqGmooQlqM-yl6jSxFfzbvnqrt13uc-5i8I57sAauEXGCLL0Z_S2_ZvJgSFbHn73BMGtQIy2AX7MaIMHJ4ogs0fAiBnSDpKNs_g2oXu0YYf";

                _allMockLogs.Add(new HistoryLogItem
                {
                    Timestamp = now.AddMinutes(-5 * i),
                    CameraId = $"CAM-{camNum:D2}",
                    Temperature = temp,
                    Humidity = humidity,
                    ThumbnailUrl = imgUrl
                });
            }
        }

        private void LoadPage()
        {
            LogItems.Clear();

            // Filter
            var filtered = _allMockLogs.AsEnumerable();

            // Apply Group Filter
            if (SelectedCameraGroup != "All Units")
            {
                int minCam = 1;
                int maxCam = 64;
                if (SelectedCameraGroup.Contains("Agent-PC-01")) { minCam = 1; maxCam = 16; }
                else if (SelectedCameraGroup.Contains("Agent-PC-02")) { minCam = 17; maxCam = 32; }
                else if (SelectedCameraGroup.Contains("Agent-PC-03")) { minCam = 33; maxCam = 64; }

                filtered = filtered.Where(log =>
                {
                    if (int.TryParse(log.CameraId.Replace("CAM-", ""), out int num))
                    {
                        return num >= minCam && num <= maxCam;
                    }
                    return false;
                });
            }

            // Apply Date Filter
            filtered = filtered.Where(log => log.Timestamp >= FromDateTime && log.Timestamp <= ToDateTime);

            var list = filtered.ToList();
            TotalRecords = list.Count;
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
            if (TotalPages == 0) TotalPages = 1;

            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            var pageItems = list.Skip((CurrentPage - 1) * PageSize).Take(PageSize);
            foreach (var item in pageItems)
            {
                LogItems.Add(item);
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
            // Set all background to warning in real system
        }
    }
}
