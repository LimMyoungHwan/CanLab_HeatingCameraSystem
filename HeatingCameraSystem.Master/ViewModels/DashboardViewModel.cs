using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class CameraNode : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _status = "IDLE";
        
        [ObservableProperty]
        private float _currentTemperature = 0f;
    }

    public partial class AgentNode : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private string _status = "Active";

        public ObservableCollection<CameraNode> Cameras { get; } = new ObservableCollection<CameraNode>();
    }

    public partial class DashboardSlot : ObservableObject
    {
        [ObservableProperty]
        private int _index;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCamera))]
        private CameraNode? _camera;

        public bool HasCamera => Camera != null;
    }

    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private float _currentTemperature;

        [ObservableProperty]
        private float _currentHumidity;

        [ObservableProperty]
        private string _recipeStatus = "대기 중";
        
        [ObservableProperty]
        private int _currentViewMode = 1;
        
        [ObservableProperty]
        private string _currentPageInfo = "Page 1/8";

        public ObservableCollection<DashboardSlot> CameraFeeds { get; } = new ObservableCollection<DashboardSlot>();
        public ObservableCollection<AgentNode> Agents { get; } = new ObservableCollection<AgentNode>();

        private readonly List<CameraNode?> _mode2Assignments = new();
        private readonly List<CameraNode?> _mode3Assignments = new();
        private readonly List<CameraNode?> _mode4Assignments = new();
        private readonly List<CameraNode?> _mode5Assignments = new();

        private System.Windows.Threading.DispatcherTimer? _autoCycleTimer;
        private int _currentPageIndex = 0;

        public DashboardViewModel()
        {
            CurrentTemperature = 45.2f;
            CurrentHumidity = 38f;
            
            // Dummy Agents and Cameras
            for (int i = 1; i <= 3; i++)
            {
                var agent = new AgentNode { Name = $"Agent PC #{i} (16)", IsExpanded = i == 1 };
                for (int j = 1; j <= 16; j++)
                {
                    agent.Cameras.Add(new CameraNode { Id = $"CAM-{(i-1)*16 + j:D2}", CurrentTemperature = 40.5f + (j % 5) });
                }
                Agents.Add(agent);
            }

            // Pre-populate view modes with some default cameras
            var allCameras = Agents.SelectMany(a => a.Cameras).ToList();
            
            for (int i = 0; i < 8; i++)
                _mode2Assignments.Add(i < allCameras.Count ? allCameras[i] : null);
                
            for (int i = 0; i < 4; i++)
                _mode3Assignments.Add(i < allCameras.Count ? allCameras[i] : null);
                
            for (int i = 0; i < 2; i++)
                _mode4Assignments.Add(i < allCameras.Count ? allCameras[i] : null);
                
            for (int i = 0; i < 1; i++)
                _mode5Assignments.Add(i < allCameras.Count ? allCameras[i] : null);
            
            LoadCameraFeeds();
        }
        
        private void LoadCameraFeeds()
        {
            if (CurrentViewMode == 1)
            {
                // Mode 1: Auto cycling
                if (_autoCycleTimer == null)
                {
                    _autoCycleTimer = new System.Windows.Threading.DispatcherTimer();
                    _autoCycleTimer.Interval = TimeSpan.FromSeconds(1);
                    _autoCycleTimer.Tick += AutoCycleTimer_Tick;
                }
                
                _currentPageIndex = 0;
                var allCameras = Agents.SelectMany(a => a.Cameras).ToList();
                int pageSize = 8;
                int totalPages = (int)Math.Ceiling((double)allCameras.Count / pageSize);
                if (totalPages == 0) totalPages = 1;
                
                UpdateAutoCyclePage(allCameras, totalPages);
                
                if (totalPages > 1)
                {
                    _autoCycleTimer.Start();
                }
                else
                {
                    _autoCycleTimer.Stop();
                }
            }
            else
            {
                // Modes 2~5
                if (_autoCycleTimer != null)
                {
                    _autoCycleTimer.Stop();
                }
                
                CurrentPageInfo = "Page 1/1";
                
                CameraFeeds.Clear();
                int count = CurrentViewMode switch
                {
                    2 => 8,
                    3 => 4,
                    4 => 2,
                    5 => 1,
                    _ => 8
                };
                
                var currentAssignments = GetAssignmentsForMode(CurrentViewMode);
                for (int i = 0; i < count; i++)
                {
                    CameraFeeds.Add(new DashboardSlot
                    {
                        Index = i,
                        Camera = currentAssignments[i]
                    });
                }
            }
        }

        private void AutoCycleTimer_Tick(object? sender, EventArgs e)
        {
            if (CurrentViewMode != 1) return;
            
            var allCameras = Agents.SelectMany(a => a.Cameras).ToList();
            if (allCameras.Count == 0) return;

            int pageSize = 8;
            int totalPages = (int)Math.Ceiling((double)allCameras.Count / pageSize);

            _currentPageIndex = (_currentPageIndex + 1) % totalPages;
            UpdateAutoCyclePage(allCameras, totalPages);
        }

        private void UpdateAutoCyclePage(List<CameraNode> allCameras, int totalPages)
        {
            int pageSize = 8;
            CurrentPageInfo = $"Page {_currentPageIndex + 1}/{totalPages}";

            var pageCameras = allCameras.Skip(_currentPageIndex * pageSize).Take(pageSize).ToList();
            
            CameraFeeds.Clear();
            for (int i = 0; i < pageCameras.Count; i++)
            {
                CameraFeeds.Add(new DashboardSlot
                {
                    Index = i,
                    Camera = pageCameras[i]
                });
            }
        }

        private List<CameraNode?> GetAssignmentsForMode(int mode)
        {
            return mode switch
            {
                2 => _mode2Assignments,
                3 => _mode3Assignments,
                4 => _mode4Assignments,
                5 => _mode5Assignments,
                _ => _mode2Assignments
            };
        }

        [RelayCommand]
        private void SetViewMode(int mode)
        {
            CurrentViewMode = mode;
            LoadCameraFeeds();
        }

        [RelayCommand]
        private void StartRecipe()
        {
            RecipeStatus = "실행 중...";
        }

        [RelayCommand]
        private void StopRecipe()
        {
            RecipeStatus = "정지됨";
        }

        [RelayCommand]
        private void AssignCameraToDashboardSlot(Tuple<CameraNode, DashboardSlot> param)
        {
            if (param == null || CurrentViewMode == 1) return;
            var camera = param.Item1;
            var slot = param.Item2;

            // Update the slot property
            slot.Camera = camera;

            // Also update the persistent list for the current mode
            var currentAssignments = GetAssignmentsForMode(CurrentViewMode);
            if (slot.Index >= 0 && slot.Index < currentAssignments.Count)
            {
                currentAssignments[slot.Index] = camera;
            }
        }

        [RelayCommand]
        private void UnassignDashboardSlot(DashboardSlot slot)
        {
            if (slot == null || CurrentViewMode == 1) return;

            slot.Camera = null;

            var currentAssignments = GetAssignmentsForMode(CurrentViewMode);
            if (slot.Index >= 0 && slot.Index < currentAssignments.Count)
            {
                currentAssignments[slot.Index] = null;
            }
        }
    }
}
