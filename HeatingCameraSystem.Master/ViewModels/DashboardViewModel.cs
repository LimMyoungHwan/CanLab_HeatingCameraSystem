using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
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

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private DateTime _lastLiveFrameUtc = DateTime.MinValue;

        [ObservableProperty]
        private bool _hasFreshLiveFrame;
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
        private double _recipeProgressValue = 0;

        [ObservableProperty]
        private string _recipePhaseText = string.Empty;
        
        [ObservableProperty]
        private int _currentViewMode = 1;
        
        [ObservableProperty]
        private string _currentPageInfo = "Page 1/8";

        [ObservableProperty]
        private int _onlineAgentCount;

        [ObservableProperty]
        private PointCollection _temperatureTrendPoints = new();

        [ObservableProperty]
        private PointCollection _humidityTrendPoints = new();

        [ObservableProperty] private float _targetTemperature;
        [ObservableProperty] private float _targetHumidity;

        [ObservableProperty] private float _blackBody1Pv;
        [ObservableProperty] private float _blackBody1Sv;
        [ObservableProperty] private float _blackBody2Pv;
        [ObservableProperty] private float _blackBody2Sv;

        [ObservableProperty] private int _servoXPosition;
        [ObservableProperty] private int _servoYPosition;
        [ObservableProperty] private bool _servoXBusy;
        [ObservableProperty] private bool _servoYBusy;
        [ObservableProperty] private bool _servoXHomeComplete;
        [ObservableProperty] private bool _servoYHomeComplete;
        [ObservableProperty] private int _servoXErrorCode;
        [ObservableProperty] private int _servoYErrorCode;
        [ObservableProperty] private int _currentPoint;

        [ObservableProperty] private int _currentStep;
        [ObservableProperty] private int _totalSteps;
        [ObservableProperty] private float _fanSpeedHz;
        [ObservableProperty] private float _gasFlow;

        [ObservableProperty] private bool _heater;
        [ObservableProperty] private bool _cooler1st;
        [ObservableProperty] private bool _cooler2nd;
        [ObservableProperty] private bool _coolerRoom;
        [ObservableProperty] private bool _coolerRoomBypass;
        [ObservableProperty] private bool _doorLamp;
        [ObservableProperty] private bool _pairGlass;
        [ObservableProperty] private bool _mcf;
        [ObservableProperty] private bool _blower1;
        [ObservableProperty] private bool _blower2;

        [ObservableProperty] private bool _isPlcConnected;
        [ObservableProperty] private string _plcStatusMessage = "PLC 대기 중";
        [ObservableProperty] private bool _isEmergencyStop;
        [ObservableProperty] private bool _hasActiveErrors;

        public ObservableCollection<DashboardSlot> CameraFeeds { get; } = new ObservableCollection<DashboardSlot>();
        public ObservableCollection<AgentNode> Agents { get; } = new ObservableCollection<AgentNode>();
        public ObservableCollection<Recipe> Recipes { get; } = new ObservableCollection<Recipe>();
        public ObservableCollection<string> ActiveErrors { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private Recipe? _selectedRecipe;

        private readonly List<CameraNode?> _mode2Assignments = new();
        private readonly List<CameraNode?> _mode3Assignments = new();
        private readonly List<CameraNode?> _mode4Assignments = new();
        private readonly List<CameraNode?> _mode5Assignments = new();

        private readonly Dictionary<string, AgentNode> _agentMap = new();
        private readonly IPlcController? _plcController;
        private readonly INatsCommunicationService? _natsService;
        private readonly Func<IEnumerable<Recipe>> _loadRecipes;
        private readonly Queue<(float Temperature, float Humidity)> _samples = new();
        private System.Windows.Threading.DispatcherTimer? _autoCycleTimer;
        private int _currentPageIndex = 0;
        private CancellationTokenSource? _recipeCts;
        private System.Windows.Threading.DispatcherTimer? _plcPollTimer;
        private System.Windows.Threading.DispatcherTimer? _offlineCheckTimer;

        public DashboardViewModel()
            : this(
                AppServices.PlcController,
                AppServices.NatsService,
                () => AppServices.RecipeRepo?.GetAllAsync().GetAwaiter().GetResult() ?? Array.Empty<Recipe>(),
                true)
        {
        }

        public DashboardViewModel(
            IPlcController? plcController,
            INatsCommunicationService? natsService,
            Func<IEnumerable<Recipe>>? loadRecipes,
            bool startTimers)
        {
            _plcController = plcController;
            _natsService = natsService;
            _loadRecipes = loadRecipes ?? (() => Array.Empty<Recipe>());
            CurrentTemperature = 0f;
            CurrentHumidity = 0f;

            for (int i = 0; i < 8; i++) _mode2Assignments.Add(null);
            for (int i = 0; i < 4; i++) _mode3Assignments.Add(null);
            for (int i = 0; i < 2; i++) _mode4Assignments.Add(null);
            for (int i = 0; i < 1; i++) _mode5Assignments.Add(null);

            LoadCameraFeeds();
            LoadRecipes();

            if (startTimers)
            {
                _plcPollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _plcPollTimer.Tick += async (_, _) => await PollPlcAsync();
                _plcPollTimer.Start();

                _offlineCheckTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _offlineCheckTimer.Tick += (_, _) => CheckOfflineAgents();
                _offlineCheckTimer.Start();
            }

            _ = SubscribeAgentStatusAsync();
            _ = SubscribeLiveFramesAsync();
        }

        private void LoadRecipes()
        {
            Recipes.Clear();
            foreach (var r in _loadRecipes())
                Recipes.Add(r);
            SelectedRecipe = Recipes.FirstOrDefault();
        }

        [RelayCommand]
        private void RefreshRecipes() => LoadRecipes();

        private async Task SubscribeAgentStatusAsync()
        {
            if (_natsService == null) return;

            await _natsService.SubscribeAgentStatusAsync(msg =>
            {
                RunOnUi(() =>
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
                    UpdateOnlineAgentCount();
                    LoadCameraFeeds();
                });
            });
        }

        private async Task SubscribeLiveFramesAsync()
        {
            if (_natsService == null) return;

            await _natsService.SubscribeLiveFrameAsync(msg =>
            {
                _ = Task.Run(() =>
                {
                    if (msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;
                    BitmapSource? image = Decode(msg.ImageBytes);
                    if (image is null) return;

                    RunOnUi(() => ApplyLiveFrame(msg, image));
                });
            });
        }

        private void ApplyLiveFrame(LiveFrameMessage msg, BitmapSource image)
        {
            if (!_agentMap.TryGetValue(msg.AgentId, out var agent))
            {
                agent = new AgentNode { Name = msg.AgentId, IsExpanded = true };
                _agentMap[msg.AgentId] = agent;
                Agents.Add(agent);
            }

            string camId = $"CAM-{msg.CameraIndex:D2}";
            var cam = agent.Cameras.FirstOrDefault(c => c.Id == camId);
            if (cam == null)
            {
                cam = new CameraNode { Id = camId };
                agent.Cameras.Add(cam);
            }

            cam.LiveImage = image;
            cam.LastLiveFrameUtc = msg.Timestamp.ToUniversalTime();
            cam.HasFreshLiveFrame = true;
            LoadCameraFeeds();
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
            UpdateOnlineAgentCount();
            RefreshLiveFrameFreshness(DateTime.UtcNow);
        }

        public void RefreshLiveFrameFreshness(DateTime utcNow)
        {
            foreach (var camera in Agents.SelectMany(a => a.Cameras))
                camera.HasFreshLiveFrame = camera.LiveImage != null && camera.LastLiveFrameUtc >= utcNow.AddSeconds(-2);
        }

        public Task RefreshPlcSnapshotAsync() => PollPlcAsync();

        private async Task PollPlcAsync()
        {
            if (_plcController == null) return;
            try
            {
                var s = await _plcController.ReadStatusAsync();

                CurrentTemperature = s.CurrentTemperature;
                TargetTemperature = s.TargetTemperature;
                CurrentHumidity = s.CurrentHumidity;
                TargetHumidity = s.TargetHumidity;

                var bb = AppServices.BlackBodyController;
                if (bb != null)
                {
                    BlackBody1Pv = await bb.GetCurrentTemperatureAsync(0);
                    BlackBody1Sv = await bb.GetTargetTemperatureAsync(0);
                    BlackBody2Pv = await bb.GetCurrentTemperatureAsync(1);
                    BlackBody2Sv = await bb.GetTargetTemperatureAsync(1);
                }
                else
                {
                    BlackBody1Pv = s.BlackBody1Pv;
                    BlackBody1Sv = s.BlackBody1Sv;
                    BlackBody2Pv = s.BlackBody2Pv;
                    BlackBody2Sv = s.BlackBody2Sv;
                }

                ServoXPosition = s.ServoXPosition;
                ServoYPosition = s.ServoYPosition;
                ServoXBusy = s.ServoXBusy;
                ServoYBusy = s.ServoYBusy;
                ServoXHomeComplete = s.ServoXHomeComplete;
                ServoYHomeComplete = s.ServoYHomeComplete;
                ServoXErrorCode = s.ServoXErrorCode;
                ServoYErrorCode = s.ServoYErrorCode;
                CurrentPoint = s.CurrentPoint;

                CurrentStep = s.CurrentStep;
                TotalSteps = s.TotalSteps;
                FanSpeedHz = s.FanSpeedHz;
                GasFlow = s.GasFlow;

                Heater = s.Heater;
                Cooler1st = s.Cooler1st;
                Cooler2nd = s.Cooler2nd;
                CoolerRoom = s.CoolerRoom;
                CoolerRoomBypass = s.CoolerRoomBypass;
                DoorLamp = s.DoorLamp;
                PairGlass = s.PairGlass;
                Mcf = s.Mcf;
                Blower1 = s.Blower1;
                Blower2 = s.Blower2;

                UpdateActiveErrors(s.ErrorBits);
                IsEmergencyStop = s.ErrorBits.Length > 0 && s.ErrorBits[0];

                IsPlcConnected = true;
                PlcStatusMessage = $"갱신 {DateTime.Now:HH:mm:ss}";

                AddTrendSample(CurrentTemperature, CurrentHumidity);
            }
            catch (Exception ex)
            {
                IsPlcConnected = false;
                PlcStatusMessage = $"읽기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PLC poll failed: {ex.Message}");
            }
        }

        private void UpdateActiveErrors(bool[] bits)
        {
            ActiveErrors.Clear();
            var names = PlcDeviceCatalog.ErrorNames;
            for (int i = 0; i < bits.Length && i < names.Length; i++)
                if (bits[i] && !string.IsNullOrEmpty(names[i]))
                    ActiveErrors.Add(names[i]);
            HasActiveErrors = ActiveErrors.Count > 0;
        }

        private void AddTrendSample(float temperature, float humidity)
        {
            _samples.Enqueue((temperature, humidity));
            while (_samples.Count > 60) _samples.Dequeue();

            TemperatureTrendPoints = BuildPoints(_samples.Select(s => s.Temperature));
            HumidityTrendPoints = BuildPoints(_samples.Select(s => s.Humidity));
        }

        private static PointCollection BuildPoints(IEnumerable<float> values)
        {
            var list = values.ToList();
            var points = new PointCollection(list.Count);
            if (list.Count == 0) return points;

            double denominator = Math.Max(1, list.Count - 1);
            for (int i = 0; i < list.Count; i++)
            {
                double x = i / denominator * 100.0;
                double normalized = Math.Clamp(list[i], 0, 100) / 100.0;
                points.Add(new Point(x, 40.0 - normalized * 40.0));
            }
            return points;
        }

        private static BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(jpeg);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        private void UpdateOnlineAgentCount() => OnlineAgentCount = Agents.Count(a => a.IsOnline);

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
        private void SetViewMode(string mode)
        {
            CurrentViewMode = int.Parse(mode);
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
            RecipeProgressValue = 0;
            RecipePhaseText = string.Empty;

            var progress = new Progress<RecipeProgress>(p =>
            {
                RecipeProgressValue = p.TotalSteps > 0
                    ? (double)p.CurrentStep / p.TotalSteps * 100
                    : 0;
                RecipePhaseText = p.CurrentPhase;
            });

            try
            {
                await AppServices.RecipeEngine.ExecuteRecipeAsync(SelectedRecipe, _recipeCts.Token, progress);
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
