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
    /// <summary>
    /// 대시보드 카메라 트리·타일에 표시되는 카메라 1대의 상태.
    /// 하트비트와 라이브 프레임 콜백이 채운다.
    /// </summary>
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

        // CameraStatus와 독립된 축이다(그쪽은 영상 런타임에서만 파생). 보고받기 전까지는 true로 두어
        // 시리얼 상태를 못 보내는 구버전 Agent가 헛경보를 내지 않게 한다.
        [ObservableProperty]
        private bool _isSerialConnected = true;

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private DateTime _lastLiveFrameUtc = DateTime.MinValue;

        [ObservableProperty]
        private bool _hasFreshLiveFrame;
    }

    /// <summary>
    /// 카메라 트리의 Agent 노드. 하트비트로 온라인 여부를 추적하며
    /// <see cref="HostName"/> 기준으로 PC별 그룹핑된다.
    /// </summary>
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

    /// <summary>대시보드 분할 화면(뷰 모드 2~5)의 슬롯 1칸. 카메라가 비어 있을 수 있다.</summary>
    public partial class DashboardSlot : ObservableObject
    {
        [ObservableProperty]
        private int _index;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCamera))]
        private CameraNode? _camera;

        public bool HasCamera => Camera != null;
    }

    /// <summary>
    /// 대시보드(메인 모니터링) 화면. 카메라 트리와 라이브 프레임, 뷰 모드 1~5의 슬롯 배치,
    /// 공용 PLC 상태 반영, 레시피 실행/정지, Agent·카메라 인벤토리 정리를 담당한다.
    /// NATS·PLC 콜백은 백그라운드 스레드로 오므로 바인딩 상태 갱신은 <see cref="RunOnUi"/>를 거친다.
    /// </summary>
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private float _currentTemperature;

        [ObservableProperty]
        private float _currentHumidity;

        [ObservableProperty]
        private string _recipeStatus = LocalizationManager.Instance["Dash_RecipeIdle"];

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
        [ObservableProperty] private string _plcStatusMessage = LocalizationManager.Instance["Dash_PlcWaiting"];
        [ObservableProperty] private bool _isEmergencyStop;
        [ObservableProperty] private bool _hasActiveErrors;

        // PLC 에러 엣지 후 Recipe Start 인터록: 에러 클리어 + 서보 원점(±0.5mm) 복귀 전까지 잠금 유지.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartRecipeCommand))]
        private bool _recoveryLockActive;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ResumeRecipeCommand))]
        private bool _isRecipePaused;

        [ObservableProperty]
        private bool _skipUnresponsiveCameras;

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
        private TaskCompletionSource<object?>? _pauseGate;
        private System.Windows.Threading.DispatcherTimer? _offlineCheckTimer;

        /// <summary>운영 진입점. <see cref="AppServices"/>의 실제 서비스로 조립한다.</summary>
        public DashboardViewModel()
            : this(
                AppServices.PlcController,
                AppServices.NatsService,
                () => AppServices.RecipeRepo?.GetAllAsync().GetAwaiter().GetResult() ?? Array.Empty<Recipe>(),
                true,
                AppServices.DashboardLayoutRepo,
                AppServices.DialogService)
        {
            if (AppServices.RecipeEngine != null)
                AppServices.RecipeEngine.EmergencyStopChanged += OnRecipeEngineEmergencyStopChanged;
        }

        /// <summary>
        /// 테스트 주입용 생성자. <paramref name="startTimers"/>가 false면 UI 디스패처가 필요한
        /// 그룹핑·타이머·공용 PLC 상태 구독을 건너뛴다(헤드리스 테스트 전용).
        /// </summary>
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
                // Agent PC 이름 그룹핑은 스레드 친화적(thread-affine) ICollectionView를 만들므로 실제 UI 디스패처가 있을 때만 유효하다(아래 DispatcherTimer와 마찬가지로 헤드리스 테스트에서는 건너뛴다).
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
                {
                    AppServices.PlcStatus.Updated += OnSharedPlcStatus;
                    AppServices.PlcStatus.BlackBodyUpdated += OnBlackBodyUpdated;
                }
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

        /// <summary>레시피 콤보를 다시 읽는다. 이전 선택이 여전히 존재하면 선택을 유지한다.</summary>
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

        /// <summary>레시피 목록 새로고침. 대시보드 재진입 시 MainViewModel이 호출한다.</summary>
        [RelayCommand]
        private void RefreshRecipes() => LoadRecipes();

        /// <summary>
        /// 하트비트(<c>agent.status.{AgentId}</c>)를 구독해 Agent/카메라 트리를 만들고 호스트
        /// 인벤토리를 정리한다. 콜백은 NATS 스레드에서 오므로 전체를 <see cref="RunOnUi"/>로 감싼다.
        /// </summary>
        private async Task SubscribeAgentStatusAsync()
        {
            if (_natsService == null) return;

            await _natsService.SubscribeAgentStatusAsync(msg =>
            {
                if (msg.PendingSyncFiles is int pending) _pendingSyncFiles[msg.AgentId] = pending;

                RunOnUi(() =>
                {
                    string hostName = string.IsNullOrWhiteSpace(msg.HostName) ? LocalizationManager.Instance["Dash_UnknownPc"] : msg.HostName;

                    // 발신자가 자기 인벤토리에 없다 = 살아있는 카메라가 아니다. 카메라가 0대가 된
                    // 호스트도 그 사실을 알려야 하므로 이런 보고를 보낸다. 노드는 만들지 않고 정리만.
                    if (msg.HostAgentIds != null && !msg.HostAgentIds.Contains(msg.AgentId))
                    {
                        if (ReconcileHostInventory(hostName, msg.HostAgentIds))
                            LoadCameraFeeds();
                        UpdateOnlineAgentCount();
                        return;
                    }

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
                    if (msg.IsSerialConnected.HasValue)
                        cam.IsSerialConnected = msg.IsSerialConnected.Value;

                    // null은 읽지 못했다는 뜻이므로 직전 값을 유지한다. 0으로 덮으면 시리얼이
                    // 한 번 끊길 때마다 화면이 0.0℃로 튄다.
                    if (msg.CameraTemperature.HasValue)
                        cam.CurrentTemperature = (float)msg.CameraTemperature.Value;

                    bool removed = PruneStaleCameras(agent, camId, msg.HostAgentIds);
                    removed |= ReconcileHostInventory(hostName, msg.HostAgentIds);

                    UpdateOnlineAgentCount();
                    bool currentLayoutChanged = RebindPersistedLayouts();
                    if (CurrentViewMode == 1 || currentLayoutChanged || removed)
                        LoadCameraFeeds();
                });
            });
        }

        // AgentUI는 카메라 1대 = AgentId 1개다(인벤토리를 보내는 발신자만 이 규칙을 보장). 카메라의
        // OpenCV 인덱스가 바뀌면 같은 AgentId 아래 CAM-NN 노드가 하나 더 생기므로 옛 노드를 걷어낸다.
        private bool PruneStaleCameras(AgentNode agent, string currentCameraId, IReadOnlyList<string>? hostAgentIds)
        {
            if (hostAgentIds == null) return false;

            var stale = agent.Cameras.Where(c => c.Id != currentCameraId).ToList();
            foreach (var camera in stale)
            {
                ClearAssignments(camera);
                agent.Cameras.Remove(camera);
            }
            return stale.Count > 0;
        }

        // 호스트가 보고한 살아있는 카메라 집합이 유일한 진실이다. 목록에 없는 그 호스트의 카메라는
        // 하트비트 타임아웃을 기다리지 않고 즉시 제거한다 — 이것이 "즉각 반영"을 만든다.
        // null(미보고)일 때만 건너뛴다. 빈 목록은 "카메라 0대"라는 유효한 보고다.
        private bool ReconcileHostInventory(string hostName, IReadOnlyList<string>? hostAgentIds)
        {
            if (hostAgentIds == null) return false;

            var stale = Agents
                .Where(a => a.HostName == hostName && !hostAgentIds.Contains(a.Name))
                .ToList();
            foreach (var agent in stale)
                RemoveAgent(agent);
            return stale.Count > 0;
        }

        private void RemoveAgent(AgentNode agent)
        {
            foreach (var camera in agent.Cameras)
                ClearAssignments(camera);
            _agentMap.Remove(agent.Name);
            Agents.Remove(agent);
        }

        // 대시보드 슬롯 배치만 비운다. _persistedLayout(DB 저장본)은 남겨 두어 같은 카메라가
        // 돌아오면 RebindPersistedLayouts가 원래 슬롯에 다시 붙인다.
        private void ClearAssignments(CameraNode camera)
        {
            foreach (var assignments in new[] { _mode2Assignments, _mode3Assignments, _mode4Assignments, _mode5Assignments })
                for (int i = 0; i < assignments.Count; i++)
                    if (ReferenceEquals(assignments[i], camera))
                        assignments[i] = null;
        }

        /// <summary>DB에 저장된 뷰 모드 2~5의 슬롯 배치를 읽어 온다. 실제 바인딩은 UI 스레드에서 수행한다.</summary>
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

        /// <summary>
        /// 저장된 배치 중 아직 비어 있는 슬롯에, 현재 트리에 존재하는 카메라를 다시 붙인다.
        /// 카메라 제거 시 저장본을 지우지 않으므로 같은 카메라가 돌아오면 원래 슬롯에 복귀한다.
        /// 현재 뷰 모드의 슬롯이 바뀌었으면 true를 돌려 화면 재구성을 유발한다.
        /// </summary>
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

        /// <summary>
        /// 라이브 프레임을 구독한다. JPEG 디코드/색상 변환은 스레드 풀에서 처리하고,
        /// 바인딩 반영만 <see cref="RunOnUi"/>로 넘겨 UI 스레드 점유를 최소화한다.
        /// </summary>
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

        /// <summary>
        /// 5초 타이머 틱. 마지막 하트비트가 15초를 넘긴 Agent를 오프라인으로 전환하고
        /// 경고 알람을 남기며, 소속 카메라를 Offline으로 표시한다.
        /// </summary>
        private void CheckOfflineAgents()
        {
            var threshold = DateTime.UtcNow.AddSeconds(-15);
            foreach (var agent in Agents)
            {
                if (agent.LastHeartbeat < threshold && agent.IsOnline)
                {
                    agent.IsOnline = false;
                    AlarmSink.Raise(AlarmSeverity.Warning, "Agent", Localize("Dash_AgentOffline", agent.Name));
                    foreach (var cam in agent.Cameras)
                        cam.CameraStatus = CameraStatus.Offline;
                }
            }
            UpdateOnlineAgentCount();
            RefreshLiveFrameFreshness(DateTime.UtcNow);
        }

        /// <summary>마지막 프레임이 2초 이내인 카메라만 "신선한 라이브"로 표시한다(끊긴 화면 감지용).</summary>
        public void RefreshLiveFrameFreshness(DateTime utcNow)
        {
            foreach (var camera in Agents.SelectMany(a => a.Cameras))
                camera.HasFreshLiveFrame = camera.LiveImage != null && camera.LastLiveFrameUtc >= utcNow.AddSeconds(-2);
        }

        /// <summary>PLC 스냅샷을 즉시 1회 직접 판독한다(수동 새로고침·테스트용. 평상시에는 공용 서비스 이벤트가 공급).</summary>
        public Task RefreshPlcSnapshotAsync() => PollPlcAsync();

        private async Task PollPlcAsync()
        {
            if (_plcController == null) return;
            try
            {
                var s = await _plcController.ReadStatusAsync();
                IsPlcConnected = true;
                PlcStatusMessage = Localize("Dash_Refreshed", DateTime.Now.ToString("HH:mm:ss"));
                ApplyStatus(s);
            }
            catch (Exception ex)
            {
                IsPlcConnected = false;
                PlcStatusMessage = Localize("Dash_ReadFailed", ex.Message);
                System.Diagnostics.Debug.WriteLine($"[Dashboard] PLC poll failed: {ex.Message}");
            }
        }

        /// <summary>공용 <c>PlcStatusService</c> 갱신 이벤트 수신. 연결이 끊겨 있으면 스냅샷 반영을 건너뛴다.</summary>
        private void OnSharedPlcStatus(object? sender, PlcStatusSnapshot s)
        {
            try
            {
                var st = AppServices.PlcStatus;
                IsPlcConnected = st?.IsConnected ?? false;
                PlcStatusMessage = st?.StatusMessage ?? PlcStatusMessage;
                if (st == null || !st.IsConnected) return;
                ApplyStatus(s);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard] shared PLC status failed: {ex.Message}");
            }
        }

        // PLC가 끊겨도 흑체 값은 계속 갱신한다(흑체 폴링은 PLC와 독립된 타이머).
        private void OnBlackBodyUpdated(object? sender, EventArgs e)
        {
            var st = AppServices.PlcStatus;
            if (st == null) return;
            RunOnUi(() =>
            {
                BlackBody1Pv = st.BlackBody1Pv;
                BlackBody1Sv = st.BlackBody1Sv;
                BlackBody2Pv = st.BlackBody2Pv;
                BlackBody2Sv = st.BlackBody2Sv;
            });
        }

        /// <summary>
        /// PLC 스냅샷 전체를 바인딩 속성에 반영하고, 에러 래치·복구 인터록·트렌드 샘플링까지
        /// 한 번에 처리한다(스냅샷 1건 = 화면 상태 1회 갱신).
        /// </summary>
        private void ApplyStatus(PlcStatusSnapshot s)
        {
            CurrentTemperature = s.CurrentTemperature;
            TargetTemperature = s.TargetTemperature;
            CurrentHumidity = s.CurrentHumidity;
            TargetHumidity = s.TargetHumidity;

            // PlcStatusService가 직접 판독한 흑체 값을 스냅샷에 병합해 둔다 — 화면은 재판독하지 않는다.
            BlackBody1Pv = s.BlackBody1Pv;
            BlackBody1Sv = s.BlackBody1Sv;
            BlackBody2Pv = s.BlackBody2Pv;
            BlackBody2Sv = s.BlackBody2Sv;

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
        /// <summary>
        /// PLC 에러 비트의 엣지 처리. 무에러→에러 전이(엣지)에서만 래치를 걸고 알람·다이얼로그·
        /// 레시피 취소·정지 시퀀스를 1회 발동한다. 래치 중에는 매 폴링마다 같은 에러가 재발동하지
        /// 않으며, 에러 비트가 전부 클리어되는 반대 엣지에서 래치가 풀린다
        /// (단, Recipe Start 잠금은 <see cref="ReleaseRecoveryLockIfRecovered"/>가 따로 관리한다).
        /// </summary>
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
                        fired.Add(LocalizationManager.Instance.GetOrDefault("PlcErr_" + i, names[i]));
                string message = fired.Count > 0
                    ? Localize("Plc_ErrorStop", string.Join(", ", fired))
                    : Localize("Plc_ErrorStopNoDetails");
                AlarmSink.Raise(AlarmSeverity.Error, "PLC", message);
                _dialogService?.ShowError(Localize("Plc_ErrorTitle"), message);
                AppServices.RecipeEngine?.RequestEmergencyStop();
                _recipeCts?.Cancel();

                // ponytail: TriggerEmergencyStopAsync는 BitEmergencyStop(=M901)을 쓰며 이는 HardwareSettings의
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

        private bool CanStartRecipe => !RecoveryLockActive && !(AppServices.RecipeEngine?.IsEmergencyStopRequested ?? false);

        // 복구 인터록 해제: 에러 비트 전부 클리어 + 서보 X/Y 모두 원점 ±0.5mm 이내일 때만. 에러 해제만으로는 열리지 않는다.
        /// <summary>
        /// Recipe Start 복구 인터록 해제 판정. 에러 비트가 전부 클리어되고 <b>동시에</b> 서보
        /// X/Y 축이 모두 원점 ±0.5mm(<c>OriginToleranceMm</c>) 이내로 복귀해야만 잠금을 푼다.
        /// 두 조건 중 하나만 만족한 상태에서는 레시피 시작이 계속 비활성으로 남는다.
        /// </summary>
        private void ReleaseRecoveryLockIfRecovered(PlcStatusSnapshot s)
        {
            if (!RecoveryLockActive) return;
            bool errorsClear = !s.ErrorBits.Any(b => b);
            bool atOrigin = Math.Abs(s.ServoXPosition) <= OriginToleranceMm
                         && Math.Abs(s.ServoYPosition) <= OriginToleranceMm;
            if (errorsClear && atOrigin)
            {
                RecoveryLockActive = false;
                AppServices.RecipeEngine?.ResetEmergencyStop();
            }
        }

        private void OnRecipeEngineEmergencyStopChanged(object? sender, EventArgs e)
        {
            RunOnUi(() =>
            {
                bool emergencyStopRequested = AppServices.RecipeEngine?.IsEmergencyStopRequested ?? false;
                IsEmergencyStop = emergencyStopRequested;
                if (emergencyStopRequested)
                {
                    RecoveryLockActive = true;
                    _recipeCts?.Cancel();
                }

                StartRecipeCommand.NotifyCanExecuteChanged();
            });
        }

        // 두 정지를 독립적으로 시작해 비상정지 쓰기 지연이 챔버 정지를 막지 않게 한다.
        private static async Task StopAllForPlcErrorAsync(IPlcController plc)
        {
            Task chamberStop = ObserveStopAsync("Plc_ChamberStopFailed", plc.StopChamberAsync);
            Task emergencyStop = ObserveStopAsync("Plc_EmergencyStopFailed", plc.TriggerEmergencyStopAsync);
            await Task.WhenAll(chamberStop, emergencyStop);
        }

        /// <summary>정지 호출 1건을 2초 타임아웃으로 감시하고, 실패해도 알람만 남기고 삼킨다(다른 정지를 막지 않기 위해).</summary>
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

        /// <summary>현재 켜져 있는 에러 비트를 번역된 이름 목록(<see cref="ActiveErrors"/>)으로 재구성한다.</summary>
        private void UpdateActiveErrors(bool[] bits)
        {
            ActiveErrors.Clear();
            var names = PlcDeviceCatalog.ErrorNames;
            for (int i = 0; i < bits.Length && i < names.Length; i++)
                if (bits[i] && !string.IsNullOrEmpty(names[i]))
                    ActiveErrors.Add(LocalizationManager.Instance.GetOrDefault("PlcErr_" + i, names[i]));
            HasActiveErrors = ActiveErrors.Count > 0;
        }

        /// <summary>온·습도 트렌드에 샘플 1건을 추가한다. 최근 60건만 유지하는 슬라이딩 윈도다.</summary>
        private void AddTrendSample(float temperature, float humidity)
        {
            _samples.Enqueue((temperature, humidity));
            while (_samples.Count > 60) _samples.Dequeue();

            TemperatureTrendPoints = BuildPoints(_samples.Select(s => s.Temperature));
            HumidityTrendPoints = BuildPoints(_samples.Select(s => s.Humidity));
        }

        /// <summary>값(0~100으로 클램프)을 100x40 뷰박스 폴리라인 좌표로 변환한다. y축은 위가 큰 값이 되도록 뒤집는다.</summary>
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

        /// <summary>JPEG 바이트를 디코드한다. Freeze로 스레드 간 전달을 허용하며, 손상 데이터는 null을 반환한다.</summary>
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

        /// <summary>
        /// UI 스레드 마샬링 헬퍼. NATS·PLC 콜백은 백그라운드 스레드로 도착하므로
        /// ObservableCollection과 바인딩 속성 갱신은 반드시 이 경로를 거쳐야 한다.
        /// 이미 UI 스레드거나 디스패처가 없으면(헤드리스 테스트) 즉시 실행한다.
        /// </summary>
        private static void RunOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher?.Invoke(action);
        }

        private void UpdateOnlineAgentCount() => OnlineAgentCount = Agents.Count(a => a.IsOnline);

        /// <summary>
        /// 현재 뷰 모드에 맞게 슬롯 컬렉션을 재구성한다. 모드 1은 레시피 실행 중인 스텝의
        /// 카메라 1대만 자동 표시하고, 모드 2~5는 운영자가 배치한 슬롯(8/4/2/1칸)을 보여 준다.
        /// </summary>
        private void LoadCameraFeeds()
        {
            if (CurrentViewMode == 1)
            {
                CameraFeeds.Clear();
                CurrentPageInfo = _recipeRunning ? LocalizationManager.Instance["Dash_Mode1Active"] : LocalizationManager.Instance["Dash_Mode1Idle"];
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
                // 모드 2~5
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

        /// <summary>뷰 모드(1~5)를 전환하고 슬롯을 다시 그린다.</summary>
        [RelayCommand]
        private void SetViewMode(string mode)
        {
            CurrentViewMode = int.Parse(mode);
            LoadCameraFeeds();
        }

        /// <summary>
        /// 선택 레시피 실행. 복구 인터록(<see cref="RecoveryLockActive"/>)이 걸려 있으면 실행 불가.
        /// 진행률 콜백으로 활성 스텝을 추적해 모드 1 화면의 자동 카메라 전환을 구동한다.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartRecipe))]
        private async Task StartRecipeAsync()
        {
            if (AppServices.RecipeEngine == null) { RecipeStatus = LocalizationManager.Instance["Dash_ServiceNotInit"]; return; }
            if (SelectedRecipe == null) { RecipeStatus = LocalizationManager.Instance["Dash_SelectRecipeNeeded"]; return; }
            if (AppServices.RecipeEngine.IsEmergencyStopRequested) { RecipeStatus = "비상정지 상태에서는 레시피를 시작할 수 없습니다."; return; }

            // 입력값을 레시피에 되저장해 다음 실행의 기본값이 되게 한다.
            if (_dialogService is not null)
            {
                ProductionRunInput? input = _dialogService.PromptProductionRun(SelectedRecipe.SaveRootPath, SelectedRecipe.ProductNumber, SelectedRecipe.SaveFormat);
                if (input is null) { RecipeStatus = LocalizationManager.Instance["Dash_RecipeStopped"]; return; }

                SelectedRecipe.SaveRootPath = input.SaveRootPath;
                SelectedRecipe.ProductNumber = input.ProductNumber;
                SelectedRecipe.SaveFormat = input.SaveFormat;
                if (AppServices.RecipeRepo is not null) await AppServices.RecipeRepo.SaveAsync(SelectedRecipe);
                AppServices.RecipeEngine.AbortDecisionRequested = agentId => Task.FromResult(_dialogService.AskCaptureAbortDecision(agentId));
            }

            _recipeCts?.Cancel();
            _recipeCts = new CancellationTokenSource();
            RecipeStatus = Localize("Dash_RecipeRunning", SelectedRecipe.Name);
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
                await AppServices.RecipeEngine.ExecuteRecipeAsync(SelectedRecipe, _recipeCts.Token, progress, WaitForResumeAsync);
                await WaitForCaptureSyncAsync(_recipeCts.Token);
                RecipeStatus = LocalizationManager.Instance["Dash_RecipeDone"];
            }
            catch (OperationCanceledException)
            {
                RecipeStatus = LocalizationManager.Instance["Dash_RecipeStopped"];
            }
            catch (Exception ex)
            {
                RecipeStatus = Localize("Dash_RecipeError", ex.Message);
            }
            finally
            {
                _recipeRunning = false;
                _activeRecipeStepIndex = -1;
                if (CurrentViewMode == 1)
                    RunOnUi(LoadCameraFeeds);
            }
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _pendingSyncFiles = new();

        /// <summary>Agent 하트비트 기본 주기(5초)보다 길게 잡아, 마지막 촬영이 반드시 한 번은 보고되게 한다.</summary>
        private static readonly TimeSpan SyncSettleDelay = TimeSpan.FromSeconds(6);

        /// <summary>
        /// Agent가 아직 Master로 못 옮긴 촬영 파일이 0이 될 때까지 기다린다. 진행률은 레시피
        /// 진행바를 그대로 쓰고, 중지 버튼이 그대로 취소 수단이 된다.
        /// 하트비트가 갱신하는 값이라 폴링 주기를 하트비트보다 짧게 잡을 이유가 없다.
        /// </summary>
        private async Task WaitForCaptureSyncAsync(CancellationToken cancellationToken)
        {
            int Remaining() => _pendingSyncFiles.Values.Sum();

            // 미전송 수는 하트비트(기본 5초)로만 올라온다. 마지막 촬영 직후 곧바로 세면 그 파일들이
            // 아직 반영되지 않아 거의 항상 0이고, robocopy가 실패해 파일이 카메라 PC에 남아도
            // "완료"가 떠버린다. 한 주기를 넘겨 기다린 뒤에 판정한다.
            if (!string.IsNullOrWhiteSpace(SelectedRecipe?.SaveRootPath))
                await Task.Delay(SyncSettleDelay, cancellationToken);

            int total = Remaining();
            if (total == 0) return;

            while (!cancellationToken.IsCancellationRequested)
            {
                int remaining = Remaining();
                if (remaining == 0) break;

                if (remaining > total) total = remaining;
                RecipeProgressValue = (double)(total - remaining) / total * 100;
                RecipeStatus = Localize("Dash_SyncingCaptures", total - remaining, total);

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        /// <summary>실행 중인 레시피에 취소를 요청한다. 실제 종료는 실행 태스크의 취소 처리에서 완료된다.</summary>
        [RelayCommand]
        private void StopRecipe()
        {
            _recipeCts?.Cancel();
            RecipeStatus = LocalizationManager.Instance["Dash_RecipeStopping"];
        }

        /// <summary>
        /// 레시피를 세우고 운영자 확인을 기다린다. 돌려주는 값은 운영자가 켠 "동일 증상 스킵" 여부이며,
        /// 레시피 엔진은 이 값이 true일 때 응답 없는 카메라를 남은 실행에서 제외한다.
        /// </summary>
        private async Task<bool> WaitForResumeAsync(CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseGate = gate;
            RunOnUi(() => IsRecipePaused = true);
            using var registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            try
            {
                await gate.Task.ConfigureAwait(false);
                return SkipUnresponsiveCameras;
            }
            finally
            {
                _pauseGate = null;
                RunOnUi(() => IsRecipePaused = false);
            }
        }

        private bool CanResumeRecipe() => IsRecipePaused;

        [RelayCommand(CanExecute = nameof(CanResumeRecipe))]
        private void ResumeRecipe() => _pauseGate?.TrySetResult(null);

        /// <summary>드래그&amp;드롭으로 카메라를 슬롯에 배치하고 즉시 DB에 저장한다(모드 2~5 전용).</summary>
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

        /// <summary>슬롯 배치를 해제하고 즉시 DB에 저장한다(모드 2~5 전용).</summary>
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

        /// <summary>해당 모드의 현재 배치를 저장용 슬롯 목록으로 스냅샷하고 비동기 저장을 시작한다.</summary>
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

        /// <summary><c>CAM-{CameraIndex:D2}</c> 형식의 카메라 Id에서 인덱스를 추출한다. 형식이 다르면 null.</summary>
        private static int? ParseCameraIndex(CameraNode? camera)
        {
            if (camera == null || !camera.Id.StartsWith("CAM-", StringComparison.Ordinal))
                return null;
            return int.TryParse(camera.Id.Substring(4), out int index) ? index : null;
        }
    }
}
