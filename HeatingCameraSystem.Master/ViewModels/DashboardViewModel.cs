using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

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

        [ObservableProperty]
        private Core.Models.CameraStatus _cameraStatus = Core.Models.CameraStatus.Offline;
    }

    public partial class AgentNode : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private bool _isOnline = false;

        [ObservableProperty]
        private DateTime _lastHeartbeat = DateTime.MinValue;

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
        public ObservableCollection<Recipe> Recipes { get; } = new ObservableCollection<Recipe>();

        [ObservableProperty]
        private Recipe? _selectedRecipe;

        private readonly List<CameraNode?> _mode2Assignments = new();
        private readonly List<CameraNode?> _mode3Assignments = new();
        private readonly List<CameraNode?> _mode4Assignments = new();
        private readonly List<CameraNode?> _mode5Assignments = new();

        private readonly Dictionary<string, AgentNode> _agentMap = new();
        private System.Windows.Threading.DispatcherTimer? _autoCycleTimer;
        private int _currentPageIndex = 0;
        private CancellationTokenSource? _recipeCts;
        private System.Windows.Threading.DispatcherTimer? _plcPollTimer;
        private System.Windows.Threading.DispatcherTimer? _offlineCheckTimer;

        public DashboardViewModel()
        {
            CurrentTemperature = 0f;
            CurrentHumidity = 0f;

            for (int i = 0; i < 8; i++) _mode2Assignments.Add(null);
            for (int i = 0; i < 4; i++) _mode3Assignments.Add(null);
            for (int i = 0; i < 2; i++) _mode4Assignments.Add(null);
            for (int i = 0; i < 1; i++) _mode5Assignments.Add(null);

            LoadCameraFeeds();
            LoadRecipes();

            _plcPollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _plcPollTimer.Tick += async (_, _) => await PollPlcAsync();
            _plcPollTimer.Start();

            _offlineCheckTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _offlineCheckTimer.Tick += (_, _) => CheckOfflineAgents();
            _offlineCheckTimer.Start();

            _ = SubscribeAgentStatusAsync();
        }

        private void LoadRecipes()
        {
            Recipes.Clear();
            foreach (var r in AppServices.RecipeRepo.GetAllAsync().GetAwaiter().GetResult())
                Recipes.Add(r);
            SelectedRecipe = Recipes.FirstOrDefault();
        }

        [RelayCommand]
        private void RefreshRecipes() => LoadRecipes();

        private async Task SubscribeAgentStatusAsync()
        {
            if (AppServices.NatsService == null) return;

            await AppServices.NatsService.SubscribeAgentStatusAsync(msg =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!_agentMap.TryGetValue(msg.AgentId, out var agent))
                    {
                        agent = new AgentNode { Name = msg.AgentId, IsExpanded = true };
                        _agentMap[msg.AgentId] = agent;
                        Agents.Add(agent);
                    }

                    agent.IsOnline      = true;
                    agent.LastHeartbeat  = msg.Timestamp;

                    string camId = $"CAM-{msg.CameraIndex:D2}";
                    var cam = agent.Cameras.FirstOrDefault(c => c.Id == camId);
                    if (cam == null)
                    {
                        cam = new CameraNode { Id = camId };
                        agent.Cameras.Add(cam);
                    }
                    cam.CameraStatus = msg.CameraStatus;
                });
            });
        }

        private void CheckOfflineAgents()
        {
            var threshold = DateTime.UtcNow.AddSeconds(-15);
            foreach (var agent in Agents)
            {
                if (agent.LastHeartbeat < threshold && agent.IsOnline)
                {
                    agent.IsOnline = false;
                    foreach (var cam in agent.Cameras)
                        cam.CameraStatus = CameraStatus.Offline;
                }
            }
        }

        private async Task PollPlcAsync()
        {
            if (AppServices.PlcController == null) return;
            try
            {
                CurrentTemperature = await AppServices.PlcController.GetCurrentTemperatureAsync();
                CurrentHumidity = await AppServices.PlcController.GetCurrentHumidityAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PLC poll failed: {ex.Message}");
            }
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
        private async Task StartRecipeAsync()
        {
            if (AppServices.RecipeEngine == null) { RecipeStatus = "서비스 미초기화"; return; }
            if (SelectedRecipe == null) { RecipeStatus = "레시피 선택 필요"; return; }

            _recipeCts?.Cancel();
            _recipeCts = new CancellationTokenSource();
            RecipeStatus = $"실행 중: {SelectedRecipe.Name}";

            try
            {
                await AppServices.RecipeEngine.ExecuteRecipeAsync(SelectedRecipe, _recipeCts.Token);
                RecipeStatus = "완료";
            }
            catch (OperationCanceledException)
            {
                RecipeStatus = "중지됨";
            }
            catch (Exception ex)
            {
                RecipeStatus = $"오류: {ex.Message}";
            }
        }

        [RelayCommand]
        private void StopRecipe()
        {
            _recipeCts?.Cancel();
            RecipeStatus = "중지 중...";
        }

        [RelayCommand]
        private void AssignCameraToDashboardSlot(Tuple<CameraNode, DashboardSlot> param)
        {
            if (param == null || CurrentViewMode == 1) return;
            var camera = param.Item1;
            var slot = param.Item2;

            slot.Camera = camera;

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
