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
using HeatingCameraSystem.Master.Localization;
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

        [ObservableProperty]
        private string _hostName = string.Empty;

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

        [ObservableProperty] private float _servoXPosition;
        [ObservableProperty] private float _servoYPosition;
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

        // PLC 에러 엣지 후 Recipe Start 인터록: 에러 클리어 + 서보 원점(±0.5mm) 복귀 전까지 잠금 유지.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartRecipeCommand))]
        private bool _recoveryLockActive;

        public ObservableCollection<DashboardSlot> CameraFeeds { get; } = new ObservableCollection<DashboardSlot>();
        public ObservableCollection<AgentNode> Agents { get; } = new ObservableCollection<AgentNode>();
        public ObservableCollection<Recipe> Recipes { get; } = new ObservableCollection<Recipe>();
        public ObservableCollection<string> ActiveErrors { get; } = new ObservableCollection<string>();
        public ObservableCollection<AlarmEntry> Alarms => AlarmSink.Entries;

        [ObservableProperty]
        private Recipe? _selectedRecipe;

        private readonly List<CameraNode?> _mode2Assignments = new();
        private readonly List<CameraNode?> _mode3Assignments = new();
        private readonly List<CameraNode?> _mode4Assignments = new();
        private readonly List<CameraNode?> _mode5Assignments = new();

        private readonly Dictionary<string, AgentNode> _agentMap = new();
        private readonly IPlcController? _plcController;
        private readonly INatsCommunicationService? _natsService;
        private readonly IDashboardLayoutRepository? _dashboardLayoutRepo;
        private readonly IDialogService? _dialogService;
        private readonly Func<IEnumerable<Recipe>> _loadRecipes;
        private readonly Dictionary<int, List<DashboardLayoutSlot>> _persistedLayout = new();
        private readonly Queue<(float Temperature, float Humidity)> _samples = new();
        private bool _recipeRunning;
        private bool _plcErrorLatched;
        private int _activeRecipeStepIndex = -1;
        private const float OriginToleranceMm = 0.5f;
        private CancellationTokenSource? _recipeCts;
        private System.Windows.Threading.DispatcherTimer? _offlineCheckTimer;

        public DashboardViewModel()
            : this(
                AppServices.PlcController,
                AppServices.NatsService,
                () => AppServices.RecipeRepo?.GetAllAsync().GetAwaiter().GetResult() ?? Array.Empty<Recipe>(),
                true,
                AppServices.DashboardLayoutRepo,
                AppServices.DialogService)
        {
        }

        public DashboardViewModel(
            IPlcController? plcController,
            INatsCommunicationService? natsService,
            Func<IEnumerable<Recipe>>? loadRecipes,
            bool startTimers,
            IDashboardLayoutRepository? dashboardLayoutRepo = null,
            IDialogService? dialogService = null)
        {
            _plcController = plcController;
            _natsService = natsService;
            _dashboardLayoutRepo = dashboardLayoutRepo;
            _dialogService = dialogService;
            _loadRecipes = loadRecipes ?? (() => Array.Empty<Recipe>());
            CurrentTemperature = 0f;
            CurrentHumidity = 0f;

            for (int i = 0; i < 8; i++) _mode2Assignments.Add(null);
            for (int i = 0; i < 4; i++) _mode3Assignments.Add(null);
            for (int i = 0; i < 2; i++) _mode4Assignments.Add(null);
            for (int i = 0; i < 1; i++) _mode5Assignments.Add(null);

            LoadCameraFeeds();
            LoadRecipes();
            _ = LoadPersistedLayoutsAsync();

            if (startTimers)
            {
                // Agent PC-name grouping builds a thread-affine ICollectionView; only valid with a real UI dispatcher (skipped in headless tests, matching the DispatcherTimer below).
                var agentsView = System.Windows.Data.CollectionViewSource.GetDefaultView(Agents);
                agentsView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(AgentNode.HostName)));
                if (agentsView is System.ComponentModel.ICollectionViewLiveShaping live)
                {
                    live.IsLiveGrouping = true;
                    live.LiveGroupingProperties.Add(nameof(AgentNode.HostName));
                }

                _offlineCheckTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _offlineCheckTimer.Tick += (_, _) => CheckOfflineAgents();
                _offlineCheckTimer.Start();

                if (AppServices.PlcStatus != null)
                    AppServices.PlcStatus.Updated += OnSharedPlcStatus;
            }

            _ = SubscribeAgentStatusAsync();
            _ = SubscribeLiveFramesAsync();

            LivePreviewColorMode.Changed += OnColorModeChanged;
        }

        // 0=컬러(iron), 1=그레이스케일 — XAML 콤보 아이템 순서와 일치.
        [ObservableProperty]
        private int _colorMapIndex = LivePreviewColorMode.Grayscale ? 1 : 0;

        partial void OnColorMapIndexChanged(int value) => LivePreviewColorMode.SetGrayscale(value == 1);

        private void OnColorModeChanged() =>
            RunOnUi(() => ColorMapIndex = LivePreviewColorMode.Grayscale ? 1 : 0);

        private void LoadRecipes()
        {
            string? previousRecipeId = SelectedRecipe?.Id;
            Recipes.Clear();
            foreach (var r in _loadRecipes())
                Recipes.Add(r);
            SelectedRecipe = (previousRecipeId == null
                ? null
                : Recipes.FirstOrDefault(r => r.Id == previousRecipeId))
                ?? Recipes.FirstOrDefault();
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
                    string hostName = string.IsNullOrWhiteSpace(msg.HostName) ? "(미확인 PC)" : msg.HostName;
                    if (!_agentMap.TryGetValue(msg.AgentId, out var agent))
                    {
                        agent = new AgentNode { Name = msg.AgentId, IsExpanded = true, HostName = hostName };
                        _agentMap[msg.AgentId] = agent;
                        Agents.Add(agent);
                    }

                    agent.IsOnline      = true;
                    agent.LastHeartbeat  = msg.Timestamp;
                    agent.HostName       = hostName;

                    string camId = $"CAM-{msg.CameraIndex:D2}";
                    var cam = agent.Cameras.FirstOrDefault(c => c.Id == camId);
                    if (cam == null)
                    {
                        cam = new CameraNode { Id = camId };
                        agent.Cameras.Add(cam);
                    }
                    cam.CameraStatus = msg.CameraStatus;
                    UpdateOnlineAgentCount();
                    bool currentLayoutChanged = RebindPersistedLayouts();
                    if (CurrentViewMode == 1 || currentLayoutChanged)
                        LoadCameraFeeds();
                });
            });
        }

        private async Task LoadPersistedLayoutsAsync()
        {
            if (_dashboardLayoutRepo == null) return;

            try
            {
                var loaded = new Dictionary<int, List<DashboardLayoutSlot>>();
                for (int mode = 2; mode <= 5; mode++)
                    loaded[mode] = (await _dashboardLayoutRepo.GetForModeAsync(mode)).ToList();

                RunOnUi(() =>
                {
                    foreach (var pair in loaded)
                        _persistedLayout[pair.Key] = pair.Value;

                    if (RebindPersistedLayouts())
                        LoadCameraFeeds();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] layout load failed: {ex.Message}");
            }
        }

        private bool RebindPersistedLayouts()
        {
            bool currentModeChanged = false;
            foreach (var pair in _persistedLayout)
            {
                var assignments = GetAssignmentsForMode(pair.Key);
                foreach (var slot in pair.Value)
                {
                    if (slot.Index < 0 || slot.Index >= assignments.Count || assignments[slot.Index] != null)
                        continue;
                    if (string.IsNullOrEmpty(slot.AgentId) || slot.CameraIndex == null)
                        continue;
                    if (!_agentMap.TryGetValue(slot.AgentId, out var agent))
                        continue;

                    string cameraId = $"CAM-{slot.CameraIndex.Value:D2}";
                    var camera = agent.Cameras.FirstOrDefault(c => c.Id == cameraId);
                    if (camera == null) continue;

                    assignments[slot.Index] = camera;
                    if (pair.Key == CurrentViewMode)
                        currentModeChanged = true;
                }
            }

            return currentModeChanged;
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
                    image = LivePreviewColorMode.Apply(image);

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
                    AlarmSink.Raise(AlarmSeverity.Warning, "Agent", $"{agent.Name} 오프라인");
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
                IsPlcConnected = true;
                PlcStatusMessage = $"갱신 {DateTime.Now:HH:mm:ss}";
                ApplyStatus(s);
                await RefreshBlackBodyAsync(s);
            }
            catch (Exception ex)
            {
                IsPlcConnected = false;
                PlcStatusMessage = $"읽기 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PLC poll failed: {ex.Message}");
            }
        }

        private async void OnSharedPlcStatus(object? sender, PlcStatusSnapshot s)
        {
            try
            {
                var st = AppServices.PlcStatus;
                IsPlcConnected = st?.IsConnected ?? false;
                PlcStatusMessage = st?.StatusMessage ?? PlcStatusMessage;
                if (st == null || !st.IsConnected) return;
                ApplyStatus(s);
                await RefreshBlackBodyAsync(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] shared PLC status failed: {ex.Message}");
            }
        }

        private void ApplyStatus(PlcStatusSnapshot s)
        {
            CurrentTemperature = s.CurrentTemperature;
            TargetTemperature = s.TargetTemperature;
            CurrentHumidity = s.CurrentHumidity;
            TargetHumidity = s.TargetHumidity;

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
            HandlePlcErrors(s.ErrorBits);
            ReleaseRecoveryLockIfRecovered(s);
            AddTrendSample(CurrentTemperature, CurrentHumidity);
        }

        // PLC 에러 감지 → 전체 정지 + 알람. 엣지 검출 + 래치: 폴링(~1s)마다 재발동하지 않으며 자동 재개도 없다.
        internal void HandlePlcErrors(bool[] errorBits)
        {
            bool anyError = errorBits.Any(b => b);
            IsEmergencyStop = anyError;

            if (anyError && !_plcErrorLatched)
            {
                _plcErrorLatched = true;
                RecoveryLockActive = true;

                var names = PlcDeviceCatalog.ErrorNames;
                var fired = new List<string>();
                for (int i = 0; i < errorBits.Length && i < names.Length; i++)
                    if (errorBits[i] && !string.IsNullOrEmpty(names[i]))
                        fired.Add(names[i]);
                string message = fired.Count > 0
                    ? Localize("Plc_ErrorStop", string.Join(", ", fired))
                    : Localize("Plc_ErrorStopNoDetails");
                AlarmSink.Raise(AlarmSeverity.Error, "PLC", message);
                _dialogService?.ShowError(Localize("Plc_ErrorTitle"), message);
                _recipeCts?.Cancel();

                // ponytail: TriggerEmergencyStopAsync는 BitEmergencyStop(=M2000)을 쓰는데 이는 HardwareSettings의
                // 문서화된 PLACEHOLDER 주소다 — 소프트웨어 stop-all + 알람은 올바르나 실제 PLC estop 비트는 하드웨어
                // 확인이 필요하다(주소는 변경하지 말 것).
                if (_plcController != null)
                    _ = StopAllForPlcErrorAsync(_plcController);
            }
            else if (!anyError && _plcErrorLatched)
            {
                _plcErrorLatched = false;
                AlarmSink.Raise(AlarmSeverity.Info, "PLC", Localize("Plc_ErrorCleared"));
            }
        }

        private bool CanStartRecipe => !RecoveryLockActive;

        // 복구 인터록 해제: 에러 비트 전부 클리어 + 서보 X/Y 모두 원점 ±0.5mm 이내일 때만. 에러 해제만으로는 열리지 않는다.
        private void ReleaseRecoveryLockIfRecovered(PlcStatusSnapshot s)
        {
            if (!RecoveryLockActive) return;
            bool errorsClear = !s.ErrorBits.Any(b => b);
            bool atOrigin = Math.Abs(s.ServoXPosition) <= OriginToleranceMm
                         && Math.Abs(s.ServoYPosition) <= OriginToleranceMm;
            if (errorsClear && atOrigin)
                RecoveryLockActive = false;
        }

        // 두 정지를 독립적으로 시작해 비상정지 쓰기 지연이 챔버 정지를 막지 않게 한다.
        private static async Task StopAllForPlcErrorAsync(IPlcController plc)
        {
            Task chamberStop = ObserveStopAsync("Plc_ChamberStopFailed", plc.StopChamberAsync);
            Task emergencyStop = ObserveStopAsync("Plc_EmergencyStopFailed", plc.TriggerEmergencyStopAsync);
            await Task.WhenAll(chamberStop, emergencyStop);
        }

        private static async Task ObserveStopAsync(string label, Func<Task> stop)
        {
            try
            {
                await stop().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                string message = Localize(label, ex.Message);
                AlarmSink.Raise(AlarmSeverity.Error, "PLC", message);
                System.Diagnostics.Debug.WriteLine($"[Dashboard] {message}");
            }
        }

        private static string Localize(string key, params object[] args) =>
            string.Format(LocalizationManager.Instance[key], args);

        private async Task RefreshBlackBodyAsync(PlcStatusSnapshot s)
        {
            var bb = AppServices.BlackBodyController;
            if (bb == null)
            {
                BlackBody1Pv = s.BlackBody1Pv;
                BlackBody1Sv = s.BlackBody1Sv;
                BlackBody2Pv = s.BlackBody2Pv;
                BlackBody2Sv = s.BlackBody2Sv;
                return;
            }
            try
            {
                BlackBody1Pv = await bb.GetCurrentTemperatureAsync(0);
                BlackBody1Sv = await bb.GetTargetTemperatureAsync(0);
                BlackBody2Pv = await bb.GetCurrentTemperatureAsync(1);
                BlackBody2Sv = await bb.GetTargetTemperatureAsync(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] blackbody read failed: {ex.Message}");
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
                CameraFeeds.Clear();
                CurrentPageInfo = _recipeRunning ? "Mode 1 — 활성 카메라" : "Mode 1 — 대기";
                if (!_recipeRunning || SelectedRecipe == null)
                    return;
                if (_activeRecipeStepIndex < 0 || _activeRecipeStepIndex >= SelectedRecipe.Steps.Count)
                    return;

                int cameraIndex = SelectedRecipe.Steps[_activeRecipeStepIndex].CameraIndex;
                string cameraId = $"CAM-{cameraIndex:D2}";
                var camera = Agents.SelectMany(a => a.Cameras).FirstOrDefault(c => c.Id == cameraId);
                if (camera != null)
                    CameraFeeds.Add(new DashboardSlot { Index = 0, Camera = camera });
            }
            else
            {
                // Modes 2~5
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

        [RelayCommand(CanExecute = nameof(CanStartRecipe))]
        private async Task StartRecipeAsync()
        {
            if (AppServices.RecipeEngine == null) { RecipeStatus = "서비스 미초기화"; return; }
            if (SelectedRecipe == null) { RecipeStatus = "레시피 선택 필요"; return; }

            _recipeCts?.Cancel();
            _recipeCts = new CancellationTokenSource();
            RecipeStatus = $"실행 중: {SelectedRecipe.Name}";
            RecipeProgressValue = 0;
            RecipePhaseText = string.Empty;
            _recipeRunning = true;
            _activeRecipeStepIndex = 0;
            if (CurrentViewMode == 1)
                RunOnUi(LoadCameraFeeds);

            var progress = new Progress<RecipeProgress>(p =>
            {
                RecipeProgressValue = p.TotalSteps > 0
                    ? (double)p.CurrentStep / p.TotalSteps * 100
                    : 0;
                RecipePhaseText = p.CurrentPhase;
                _activeRecipeStepIndex = p.CurrentStep;
                if (CurrentViewMode == 1)
                    RunOnUi(LoadCameraFeeds);
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
            finally
            {
                _recipeRunning = false;
                _activeRecipeStepIndex = -1;
                if (CurrentViewMode == 1)
                    RunOnUi(LoadCameraFeeds);
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
                PersistLayout(CurrentViewMode);
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
                PersistLayout(CurrentViewMode);
            }
        }

        private void PersistLayout(int mode)
        {
            if (_dashboardLayoutRepo == null) return;

            var slots = GetAssignmentsForMode(mode)
                .Select((camera, index) =>
                {
                    var agent = camera == null
                        ? null
                        : Agents.FirstOrDefault(a => a.Cameras.Contains(camera));
                    return new DashboardLayoutSlot
                    {
                        Mode = mode,
                        Index = index,
                        AgentId = agent?.Name,
                        CameraIndex = ParseCameraIndex(camera)
                    };
                })
                .ToList();

            _persistedLayout[mode] = slots;
            _ = SaveLayoutAsync(mode, slots);
        }

        private async Task SaveLayoutAsync(int mode, IReadOnlyList<DashboardLayoutSlot> slots)
        {
            var repo = _dashboardLayoutRepo;
            if (repo == null) return;

            try
            {
                await repo.SaveForModeAsync(mode, slots);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] layout save failed: {ex.Message}");
            }
        }

        private static int? ParseCameraIndex(CameraNode? camera)
        {
            if (camera == null || !camera.Id.StartsWith("CAM-", StringComparison.Ordinal))
                return null;
            return int.TryParse(camera.Id.Substring(4), out int index) ? index : null;
        }
    }
}
