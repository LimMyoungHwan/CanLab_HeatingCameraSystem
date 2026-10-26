using HeatingCameraSystem.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Microsoft.Win32;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// 레시피 편집 그리드의 스텝 1행. 좌표는 mm, 온도는 ℃ 단위다.
    /// <see cref="CameraIndex"/>·<see cref="TargetPositionIndex"/>가 실제 값이고,
    /// NodeAssignment 문자열은 표시용이자 값이 0일 때의 파싱 폴백이다.
    /// </summary>
    public partial class RecipeStepModel : ObservableObject
    {
        [ObservableProperty] private int _stepNumber;
        public string? PlanCellKey { get; set; }
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        [ObservableProperty] private bool _useCellCamera;
        [ObservableProperty] private int _shotNumberOffset;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UsesCellPosition))]
        private BlackBodyRole? _cellPositionRole;
        public bool UsesCellPosition => CellPositionRole.HasValue;
        public static BlackBodyRole[] PositionRoles { get; } = Enum.GetValues<BlackBodyRole>();
        [ObservableProperty] private string _nodeAssignment = string.Empty;
        [ObservableProperty] private float _blackbodyRef;
        [ObservableProperty] private int _blackBodyIndex;
        [ObservableProperty] private float _blackbodyRef1;
        [ObservableProperty] private bool _waitForStabilization = true;
        [ObservableProperty] private bool _waitForChamberStabilization = true;
        [ObservableProperty] private bool _useSafetyTemperature;
        [ObservableProperty] private float _safetyTempMin;
        [ObservableProperty] private float _safetyTempMax;
        [ObservableProperty] private bool _useSafetyHumidity;
        [ObservableProperty] private float _safetyHumidityMin;
        [ObservableProperty] private float _safetyHumidityMax;
        [ObservableProperty] private int _shotCount = 1;
        [ObservableProperty] private int _captureIntervalSeconds;
        [ObservableProperty] private int _captureDurationSeconds;
        [ObservableProperty] private float _positionX;
        [ObservableProperty] private float _positionY;
        [ObservableProperty] private double _targetChamberTemperature;
        [ObservableProperty] private double _targetChamberHumidity;
        [ObservableProperty] private float _targetFanSpeedHz;
        [ObservableProperty] private bool _disableHumidityControl;
        [ObservableProperty] private double _biasTargetLevel;
        [ObservableProperty] private double _stabilizationToleranceC;
        [ObservableProperty] private double _stabilizationToleranceRh;
        [ObservableProperty] private int _soakMinutes;
        [ObservableProperty] private int _waitHours;
        [ObservableProperty] private int _waitMinutes;
        [ObservableProperty] private int _waitSeconds;
        [ObservableProperty] private RecipeStepKind _kind = RecipeStepKind.LegacyCapture;
        [ObservableProperty] private MotorMoveType _motorMoveType = MotorMoveType.Manual;
        /// <summary>
        /// Point(Automatic)로 저장된 모터 스텝은 수동(좌표) 전환을 막는다.
        /// 스텝에 예전 좌표(대부분 0,0)가 남아 있어 되돌리면 엉뚱한 위치로 구동되기 때문이다.
        /// </summary>
        [ObservableProperty] private bool _isPointLocked;
        [ObservableProperty] private string _cameraOperation = CameraControlOps.Capture;

        public RecipeStepModel()
        {
            CameraTargets.CollectionChanged += OnCameraTargetsChanged;
        }

        public int CameraIndex { get; set; }
        public int TargetPositionIndex { get; set; }
        public bool ShowsAutomaticPoint => Kind == RecipeStepKind.MotorMove && MotorMoveType == MotorMoveType.Automatic;
        public bool CanChangeMotorMoveType => !IsPointLocked;
        public bool ShowsManualCoordinates => Kind == RecipeStepKind.MotorMove && MotorMoveType == MotorMoveType.Manual;
        public ObservableCollection<CameraTargetModel> CameraTargets { get; } = new();

        /// <summary>false면 캡처 결과를 기다리지 않고 다음 스텝으로 넘어간다(fork).</summary>
        [ObservableProperty] private bool _waitForCaptureResult = true;

        /// <summary>촬영 대기 스텝의 타임아웃(초). 0이면 전역 설정을 쓴다.</summary>
        [ObservableProperty] private int _captureJoinTimeoutSeconds;
        public string SelectedCameraSummary => string.Join(", ", CameraTargets.Where(target => target.IsSelected).Select(target => target.Label).DefaultIfEmpty("카메라 선택"));
        /// <summary>
        /// 입력한 장수·간격·지속 시간이 실제로 몇 회 몇 장이 되는지 운영자 문장 그대로 되읽어 준다.
        /// 반복 횟수가 두 값의 나눗셈으로 정해지는데 화면에 안 보여서 오해가 잦았다
        /// (간격을 지속 시간과 같게 넣고 여러 번 찍힐 것으로 기대하는 사고).
        /// </summary>
        public string CapturePlanSummary
        {
            get
            {
                int shots = ShotCount > 0 ? ShotCount : 1;
                if (CaptureIntervalSeconds <= 0) return $"즉시 {shots}장 1회 · 총 {shots}장";

                int repeats = Services.RecipeEngine.CaptureRepeatCount(CaptureIntervalSeconds, CaptureDurationSeconds);
                string text = $"{Span(CaptureDurationSeconds)} 동안 {Span(CaptureIntervalSeconds)} 간격으로 {shots}장씩 {repeats}회 · 총 {shots * repeats}장";

                if (CaptureDurationSeconds < CaptureIntervalSeconds * 2)
                    return text + "   ⚠ 지속 시간이 간격의 2배 미만이라 1회만 찍습니다";

                int remainder = CaptureDurationSeconds % CaptureIntervalSeconds;
                return remainder > 0
                    ? text + $"   ⚠ 나누어떨어지지 않아 마지막 {Span(remainder)}는 촬영하지 않습니다"
                    : text;
            }
        }

        private static string Span(int seconds)
            => seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60}분" : $"{seconds}초";

        partial void OnShotCountChanged(int value) => OnPropertyChanged(nameof(CapturePlanSummary));
        partial void OnCaptureIntervalSecondsChanged(int value) => OnPropertyChanged(nameof(CapturePlanSummary));
        partial void OnCaptureDurationSecondsChanged(int value) => OnPropertyChanged(nameof(CapturePlanSummary));

        public bool ShowsLegacyFields => Kind == RecipeStepKind.LegacyCapture;
        public bool ShowsMotorFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.MotorMove;
        public bool ShowsChamberFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.ChamberControl;
        public bool ShowsHumidityFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.HumidityControl;
        public bool ShowsCameraFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.CameraCommand;
        public bool ShowsCameraOperation => Kind == RecipeStepKind.CameraCommand;
        public bool IsCaptureOperation => CameraOperation == CameraControlOps.Capture;
        public bool IsBiasOperation => CameraOperation is CameraControlOps.BiasLow or CameraControlOps.BiasMid or CameraControlOps.BiasHigh;
        public bool HumidityControlEnabled => !DisableHumidityControl;
        public bool ShowsBlackBodyFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.BlackBodyControl;
        public bool ShowsBlackBodyStabilization => Kind == RecipeStepKind.BlackBodyControl;

        partial void OnKindChanged(RecipeStepKind value)
        {
            OnPropertyChanged(nameof(ShowsLegacyFields));
            OnPropertyChanged(nameof(ShowsMotorFields));
            OnPropertyChanged(nameof(ShowsChamberFields));
            OnPropertyChanged(nameof(ShowsHumidityFields));
            OnPropertyChanged(nameof(ShowsCameraFields));
            OnPropertyChanged(nameof(ShowsCameraOperation));
            OnPropertyChanged(nameof(ShowsBlackBodyFields));
            OnPropertyChanged(nameof(ShowsBlackBodyStabilization));
            OnPropertyChanged(nameof(ShowsAutomaticPoint));
            OnPropertyChanged(nameof(ShowsManualCoordinates));
        }

        partial void OnDisableHumidityControlChanged(bool value) => OnPropertyChanged(nameof(HumidityControlEnabled));

        partial void OnCameraOperationChanged(string value)
        {
            OnPropertyChanged(nameof(IsCaptureOperation));
            OnPropertyChanged(nameof(IsBiasOperation));

            if (CameraBiasDefaults.For(value) is double target) BiasTargetLevel = target;
        }

        partial void OnIsPointLockedChanged(bool value) => OnPropertyChanged(nameof(CanChangeMotorMoveType));

        partial void OnMotorMoveTypeChanged(MotorMoveType value)
        {
            if (IsPointLocked && value == MotorMoveType.Manual)
            {
                MotorMoveType = MotorMoveType.Automatic;
                return;
            }

            OnPropertyChanged(nameof(ShowsAutomaticPoint));
            OnPropertyChanged(nameof(ShowsManualCoordinates));
        }

        private void OnCameraTargetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CameraTargetModel target in e.OldItems)
                    target.PropertyChanged -= OnCameraTargetPropertyChanged;

            if (e.NewItems != null)
                foreach (CameraTargetModel target in e.NewItems)
                    target.PropertyChanged += OnCameraTargetPropertyChanged;

            OnPropertyChanged(nameof(SelectedCameraSummary));
        }

        private void OnCameraTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(CameraTargetModel.IsSelected) or nameof(CameraTargetModel.AgentId) or nameof(CameraTargetModel.CameraIndex))
                OnPropertyChanged(nameof(SelectedCameraSummary));
        }
    }

    public sealed class RecipeStepKindOption
    {
        public RecipeStepKind Value { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public sealed class CameraOperationOption
    {
        public string Value { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }

    public sealed class MotorMoveTypeOption
    {
        public MotorMoveType Value { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public sealed class ChamberRangeOption
    {
        public ChamberRange? Value { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public sealed class BlackBodyRoleOption
    {
        public BlackBodyRole? Value { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public partial class CameraTargetModel : ObservableObject
    {
        [ObservableProperty] private string _agentId = string.Empty;
        [ObservableProperty] private int _cameraIndex;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label))]
        private bool _isOnline;
        /// <summary>저장된 대상은 연결이 잠시 끊겨도 대역·역할·선택을 보존한다.</summary>
        public bool IsPersisted { get; set; }

        /// <summary>이 카메라의 온도대역. BIAS 명령과 저장 폴더가 함께 이 값을 쓴다.</summary>
        [ObservableProperty] private ChamberRange? _targetChamber;

        /// <summary>촬영 시점에 이 카메라 앞에 놓인 흑체. 흑체가 이동하고 카메라는 고정이라 여기에 붙는다.</summary>
        [ObservableProperty] private BlackBodyRole? _targetBlackBody;

        public string Label => $"{AgentId} (CAM-{CameraIndex:D2})" + (IsOnline ? string.Empty : " · 오프라인");

        partial void OnAgentIdChanged(string value) => OnPropertyChanged(nameof(Label));
        partial void OnCameraIndexChanged(int value) => OnPropertyChanged(nameof(Label));
    }

    /// <summary>미리보기 카메라 선택 콤보의 항목(온라인 Agent 카메라 1대).</summary>
    public sealed class AgentCameraOption
    {
        public string AgentId { get; init; } = string.Empty;
        public int CameraIndex { get; init; }
        public string Label => $"{AgentId} (CAM-{CameraIndex:D2})";
    }

    /// <summary>레시피 1건의 편집용 모델. 도메인 <see cref="Recipe"/>와 ToDomain/FromDomain으로 상호 변환한다.</summary>
    public partial class RecipeModel : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPlanRecipe))]
        [NotifyPropertyChangedFor(nameof(IsLegacyRecipe))]
        [NotifyPropertyChangedFor(nameof(TypeLabel))]
        private CapturePlan? _plan;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPlanValidationError))]
        [NotifyPropertyChangedFor(nameof(PlanValidationLabel))]
        private string? _planValidationError;

        public bool IsPlanRecipe => Plan != null;
        public bool IsLegacyRecipe => Plan == null;
        public string TypeLabel => IsPlanRecipe ? "계획" : "스텝";
        public bool HasPlanValidationError => !string.IsNullOrEmpty(PlanValidationError);
        public string PlanValidationLabel => PlanValidationError is { Length: > 0 } error
            ? error.StartsWith("실행 불가:", StringComparison.Ordinal) ? error : $"실행 불가: {error}"
            : string.Empty;

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _lastModified = string.Empty;
        [ObservableProperty] private int _rampMinutes;
        [ObservableProperty] private float _recordOnTemperatureDelta;
        [ObservableProperty] private bool _overwriteDuplicateShots;
        [ObservableProperty] private float _recordOnHumidityDelta;
        [ObservableProperty] private int _recordIntervalSeconds;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new();
    }

    /// <summary>
    /// 레시피 편집 화면. 레시피 CRUD·복사·JSON 가져오기/내보내기, 스텝 편집(추가·삭제·순서 이동),
    /// 그리고 좌표 잡기를 위한 서보 이동·JOG와 카메라 라이브 미리보기를 담당한다.
    /// NATS 콜백은 백그라운드 스레드로 오므로 UI 갱신은 Dispatcher로 마샬링한다.
    /// </summary>
    public partial class RecipeEditorViewModel : ObservableObject, IDisposable, IAsyncDisposable
    {
        public ObservableCollection<RecipeModel> Recipes { get; } = new ObservableCollection<RecipeModel>();
        public RecipeStepKindOption[] StepKindOptions { get; } =
        {
            new() { Value = RecipeStepKind.MotorMove, Label = "PLC 모터 이동" },
            new() { Value = RecipeStepKind.ChamberControl, Label = "PLC 온도 설정" },
            new() { Value = RecipeStepKind.HumidityControl, Label = "PLC 습도 설정" },
            new() { Value = RecipeStepKind.CameraCommand, Label = "카메라 명령" },
            new() { Value = RecipeStepKind.BlackBodyControl, Label = "블랙바디 온도 제어" },
            new() { Value = RecipeStepKind.Wait, Label = "대기" },
            new() { Value = RecipeStepKind.CaptureJoin, Label = "촬영 대기" }
        };

        public ChamberRangeOption[] ChamberRangeOptions { get; } =
        {
            new() { Value = null, Label = "(지정 안 함)" },
            new() { Value = ChamberRange.Low, Label = "저온" },
            new() { Value = ChamberRange.Mid, Label = "상온" },
            new() { Value = ChamberRange.High, Label = "고온" }
        };

        public BlackBodyRoleOption[] BlackBodyRoleOptions { get; } =
        {
            new() { Value = null, Label = "(지정 안 함)" },
            new() { Value = BlackBodyRole.Hot, Label = "hot" },
            new() { Value = BlackBodyRole.Cold, Label = "cold" },
            new() { Value = BlackBodyRole.Room, Label = "room (흑체 없음)" }
        };
        public CameraOperationOption[] CameraOperationOptions { get; } = new[]
        {
            CameraControlOps.Capture, CameraControlOps.ShutterOpen, CameraControlOps.ShutterClose,
            CameraControlOps.Bias, CameraControlOps.BiasLow, CameraControlOps.BiasMid, CameraControlOps.BiasHigh,
            CameraControlOps.Nuc, CameraControlOps.Run, CameraControlOps.Stop, CameraControlOps.SaveConfig, CameraControlOps.RefreshInfo
        }.Select(operation => new CameraOperationOption { Value = operation, Label = CapturePlanLabels.Operation(operation) }).ToArray();

        public int[] BlackBodyIndexOptions { get; } = { 0, 1 };
        public MotorMoveTypeOption[] MotorMoveTypeOptions { get; } =
        {
            new() { Value = MotorMoveType.Manual, Label = "수동" },
            new() { Value = MotorMoveType.Automatic, Label = "자동" }
        };
        public int[] ServoPointOptions { get; } = Enumerable.Range(1, 20).ToArray();

        [ObservableProperty]
        private RecipeModel? _selectedRecipe;

        [ObservableProperty]
        private string _ioStatusMessage = string.Empty;

        [ObservableProperty] private CapturePlanEditorViewModel? _planEditor;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasOperationError))]
        private string _operationError = string.Empty;
        public bool HasOperationError => !string.IsNullOrEmpty(OperationError);
        public string TemplateUsage => SelectedRecipe == null ? string.Empty : "사용 중인 계획: " +
            string.Join(", ", _recipeRepository.GetAllAsync().GetAwaiter().GetResult()
                .Where(r => r.Plan?.Entries?.Any(e => e.TemplateRecipeId == SelectedRecipe.Id) == true).Select(r => r.Name).DefaultIfEmpty("없음"));
        private readonly IRecipeRepository _recipeRepository;
        private readonly ICameraPositionRepository? _positionRepository;
        private bool _disposed;
        private readonly NatsSubscriptionLifetime _subscriptions = new();
        private readonly AgentDirectory _cameraDirectory = new();
        private CancellationTokenSource? _cameraCommandCancellation;
        [ObservableProperty] private bool _isCameraCommandBusy;
        [ObservableProperty] private string _cameraCommandStatus = string.Empty;

        public RecipeEditorViewModel() : this(AppServices.RecipeRepo, AppServices.CameraPositions) { }

        public RecipeEditorViewModel(IRecipeRepository recipeRepository, ICameraPositionRepository? positionRepository = null)
        {
            _recipeRepository = recipeRepository;
            _positionRepository = positionRepository;
            SubscribeCameraServices();

            foreach (var r in _recipeRepository.GetAllAsync().GetAwaiter().GetResult())
                Recipes.Add(FromDomain(r));

            if (Recipes.Count > 0)
                SelectRecipe(Recipes[0]);
        }

        partial void OnSelectedRecipeChanging(RecipeModel? oldValue, RecipeModel? newValue)
        {
            if (oldValue != null) SnapshotRecipe(oldValue);
        }

        /// <summary>현재 계획 편집값을 메모리에 반영한다. 저장소에는 쓰지 않는다.</summary>
        internal Recipe SnapshotRecipe(RecipeModel model)
        {
            if (ReferenceEquals(model, SelectedRecipe) && PlanEditor is { } editor)
            {
                bool valid = editor.TryCompile(out var steps);
                Recipe snapshot = ToDomain(model);
                snapshot.Plan = CapturePlanRecipeCopies.ClonePlan(editor.Plan);
                snapshot.Steps = valid ? steps.Select(step => step.Copy()).ToList() : new List<RecipeStep>();
                snapshot.PlanValidationError = valid ? null : editor.ValidationMessage;
                model.Plan = CapturePlanRecipeCopies.ClonePlan(editor.Plan);
                model.PlanValidationError = snapshot.PlanValidationError;
                model.Steps.Clear();
                foreach (RecipeStepModel step in FromDomain(snapshot).Steps)
                    model.Steps.Add(step);
                return snapshot;
            }
            return ToDomain(model);
        }

        partial void OnSelectedRecipeChanged(RecipeModel? oldValue, RecipeModel? newValue)
        {
            if (oldValue != null) oldValue.PropertyChanged -= OnSelectedRecipeModelPropertyChanged;
            if (newValue != null) newValue.PropertyChanged += OnSelectedRecipeModelPropertyChanged;
        }

        private void OnSelectedRecipeModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecipeModel.OverwriteDuplicateShots) && sender is RecipeModel model && ReferenceEquals(model, SelectedRecipe) && PlanEditor is { } editor)
                editor.OverwriteDuplicateShots = model.OverwriteDuplicateShots;
        }

        private void OnPlanEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ReferenceEquals(sender, PlanEditor) && SelectedRecipe is { } selected && PlanEditor is { } editor
                && e.PropertyName is nameof(CapturePlanEditorViewModel.ValidationMessage) or nameof(CapturePlanEditorViewModel.IsValid))
            {
                selected.PlanValidationError = editor.IsValid ? null : editor.ValidationMessage;
            }
        }

        partial void OnSelectedRecipeChanged(RecipeModel? value)
        {
            if (PlanEditor is { } previous)
            {
                previous.PropertyChanged -= OnPlanEditorPropertyChanged;
                previous.Dispose();
            }
            PlanEditor = value?.IsPlanRecipe == true
                ? new CapturePlanEditorViewModel(ToDomain(value), _positionRepository, _recipeRepository)
                : null;
            SelectedStep = null;
            OperationError = string.Empty;
            OnPropertyChanged(nameof(TemplateUsage));
            if (PlanEditor is { } editor)
            {
                editor.PropertyChanged += OnPlanEditorPropertyChanged;
                if (value != null) value.PlanValidationError = editor.IsValid ? null : editor.ValidationMessage;
                _ = LoadPlanPositionsAsync(editor);
            }
        }

        public System.Threading.Tasks.Task ReloadPlanPositionsAsync()
            => PlanEditor is { } editor ? LoadPlanPositionsAsync(editor) : System.Threading.Tasks.Task.CompletedTask;

        private async System.Threading.Tasks.Task LoadPlanPositionsAsync(CapturePlanEditorViewModel editor)
        {
            try
            {
                await editor.LoadPositionsAsync();
            }
            catch (Exception ex)
            {
                void ShowError()
                {
                    if (!_disposed && ReferenceEquals(PlanEditor, editor))
                        OperationError = $"위치표를 불러오지 못했습니다: {ex.Message}";
                }
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                    await dispatcher.InvokeAsync(ShowError);
                else
                    ShowError();
            }
        }

        /// <summary>레시피를 선택하고 목록의 선택 하이라이트를 옮긴다.</summary>
        [RelayCommand]
        private void SelectRecipe(RecipeModel recipe)
        {
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = false;
            SelectedRecipe = recipe;
            if (SelectedRecipe != null) SelectedRecipe.IsSelected = true;
        }

        public event EventHandler? RecipeAdded;

        /// <summary>기본값으로 새 레시피를 만들고 즉시 저장·선택한다.</summary>
        [RelayCommand]
        private void AddRecipe()
        {
            var vm = new RecipeModel { Name = "새 레시피", LastModified = DateTime.Now.ToString("g"), RampMinutes = 0 };
            Recipes.Add(vm);
            _recipeRepository.SaveAsync(ToDomain(vm)).GetAwaiter().GetResult();
            SelectRecipe(vm);
            RecipeAdded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>촬영 셀을 아직 선택하지 않은 새 계획을 초안으로 저장한다.</summary>
        [RelayCommand]
        private void AddPlanRecipe()
        {
            var recipe = new RecipeModel
            {
                Name = "새 촬영 계획",
                Plan = new CapturePlan { Entries = new() }
            };
            Recipes.Add(recipe);
            SelectRecipe(recipe);
            SaveRecipe();
            RecipeAdded?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void ConvertToPlan()
        {
            if (SelectedRecipe == null || !SelectedRecipe.IsLegacyRecipe) return;
            SaveRecipe();
            AddCopy(CapturePlanConverter.CreateFromTemplate(ToDomain(SelectedRecipe)));
        }

        [RelayCommand]
        private void FlattenPlan()
        {
            if (SelectedRecipe == null || PlanEditor is not { } editor) return;
            if (!editor.TryCompile(out var steps))
            {
                IoStatusMessage = "실행 가능한 계획을 완성한 후 스텝 목록으로 만들 수 있습니다.";
                return;
            }
            Recipe current = ToDomain(SelectedRecipe);
            current.Plan = CapturePlanRecipeCopies.ClonePlan(editor.Plan);
            current.Steps = steps.Select(step => step.Copy()).ToList();
            try
            {
                AddCopy(CapturePlanRecipeCopies.ToLegacyCopy(current, editor.PositionSnapshot, editor.TemplateSnapshot));
            }
            catch (ArgumentException ex)
            {
                OperationError = $"스텝 목록으로 만들 수 없습니다. {CapturePlanLabels.ValidationReason(editor.Plan, ex.Message)}";
            }
        }

        private void AddCopy(Recipe recipe)
        {
            _recipeRepository.SaveAsync(recipe).GetAwaiter().GetResult();
            var model = FromDomain(recipe);
            Recipes.Add(model);
            SelectRecipe(model);
        }

        /// <summary>선택 레시피를 새 Id로 복제해 저장·선택한다. 이름에는 "(복사)"가 붙는다.</summary>
        [RelayCommand]
        private void CopyRecipe()
        {
            if (SelectedRecipe == null) return;

            var clone = CloneRecipe(SnapshotRecipe(SelectedRecipe));
            if (clone.Plan != null && clone.PlanValidationError == null && PlanEditor is { } editor)
            {
                try
                {
                    clone.Steps = CapturePlanCompiler.Compile(clone, editor.PositionSnapshot, editor.TemplateSnapshot);
                }
                catch (ArgumentException ex)
                {
                    clone.Steps.Clear();
                    clone.PlanValidationError = $"실행 불가: {ex.Message}";
                }
            }
            _recipeRepository.SaveAsync(clone).GetAwaiter().GetResult();

            var vm = FromDomain(clone);
            Recipes.Add(vm);
            SelectRecipe(vm);
        }

        /// <summary>선택 레시피를 저장하고 수정 시각을 갱신한다.</summary>
        [RelayCommand]
        private void SaveRecipe()
        {
            if (SelectedRecipe == null) return;
            OperationError = string.Empty;
            Recipe draft = SnapshotRecipe(SelectedRecipe);
            _recipeRepository.SaveAsync(draft).GetAwaiter().GetResult();
            IoStatusMessage = draft.PlanValidationError is { Length: > 0 }
                ? "초안을 저장했습니다. 실행 스텝은 비워 두었습니다."
                : "레시피를 저장했습니다.";
            SelectedRecipe.LastModified = DateTime.Now.ToString("g");
            LockPointSteps(SelectedRecipe);
        }

        /// <summary>저장 완료 후 Point(Automatic) 스텝을 잠가 좌표 모드로 되돌리지 못하게 한다.</summary>
        public static void LockPointSteps(RecipeModel recipe)
        {
            foreach (var step in recipe.Steps)
            {
                if (step.MotorMoveType == MotorMoveType.Automatic)
                    step.IsPointLocked = true;
            }
        }

        /// <summary>레시피를 삭제한다. 선택 중이던 레시피였다면 남은 첫 레시피를 선택한다.</summary>
        [RelayCommand]
        private void DeleteRecipe(RecipeModel recipe)
        {
            if (recipe == null) return;
            var users = _recipeRepository.GetAllAsync().GetAwaiter().GetResult()
                .Where(r => r.Plan?.Entries?.Any(e => e.TemplateRecipeId == recipe.Id) == true).Select(r => r.Name).ToArray();
            if (users.Length > 0) { OperationError = "이 템플릿을 사용하는 계획이 있어 삭제할 수 없습니다: " + string.Join(", ", users); return; }
            try { _recipeRepository.DeleteAsync(recipe.Id).GetAwaiter().GetResult(); }
            catch (InvalidOperationException error) { OperationError = error.Message; return; }
            Recipes.Remove(recipe);
            if (SelectedRecipe == recipe)
                SelectedRecipe = Recipes.FirstOrDefault();
        }

        [RelayCommand]
        private void TogglePositionSlot(RecipeStepModel step)
        {
            if (SelectedRecipe == null || step.Kind != RecipeStepKind.MotorMove) return;
            if (step.CellPositionRole.HasValue) { step.CellPositionRole = null; return; }
            int index = SelectedRecipe.Steps.IndexOf(step);
            var next = SelectedRecipe.Steps.Skip(index + 1).FirstOrDefault(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture);
            step.CellPositionRole = next?.CameraTargets.FirstOrDefault(t => t.IsSelected)?.TargetBlackBody ?? BlackBodyRole.Cold;
        }

        [RelayCommand]
        private void AddStep(RecipeStepKind kind)
        {
            if (SelectedRecipe == null) return;
            int n = SelectedRecipe.Steps.Count + 1;
            var step = new RecipeStepModel
            {
                StepNumber = n,
                Kind = kind,
                NodeAssignment = string.Empty,
                CameraIndex = n,
                TargetPositionIndex = n,
                BlackbodyRef = 25.0f,
                BlackbodyRef1 = 25.0f,
                TargetChamberTemperature = 25.0,
                TargetChamberHumidity = 50.0,
                CameraOperation = CameraControlOps.Capture
            };
            if (kind == RecipeStepKind.BlackBodyControl)
            {
                step.BlackbodyRef = 25.0f;
                step.BlackBodyIndex = 0;
                step.WaitForStabilization = true;
            }
            if (kind == RecipeStepKind.FanControl)
            {
                step.TargetFanSpeedHz = 30.0f;
            }
            SyncCameraTargets(step);
            SelectedRecipe.Steps.Add(step);
        }

        /// <summary>스텝을 삭제하고 남은 스텝 번호를 1부터 다시 매긴다.</summary>
        [RelayCommand]
        private void DeleteStep(RecipeStepModel step)
        {
            if (SelectedRecipe == null || step == null) return;
            SelectedRecipe.Steps.Remove(step);
            for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                SelectedRecipe.Steps[i].StepNumber = i + 1;
        }

        /// <summary>드래그&amp;드롭으로 스텝(Item1)을 대상 스텝(Item2) 위치로 옮기고 번호를 다시 매긴다.</summary>
        [RelayCommand]
        private void MoveStep(Tuple<RecipeStepModel, RecipeStepModel> param)
        {
            if (param == null || SelectedRecipe == null) return;
            int oldIdx = SelectedRecipe.Steps.IndexOf(param.Item1);
            int newIdx = SelectedRecipe.Steps.IndexOf(param.Item2);
            if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
            {
                SelectedRecipe.Steps.Move(oldIdx, newIdx);
                for (int i = 0; i < SelectedRecipe.Steps.Count; i++)
                    SelectedRecipe.Steps[i].StepNumber = i + 1;
            }
        }

        /// <summary>선택 레시피를 CSV 파일로 내보낸다. 결과는 IoStatusMessage로 표시한다.</summary>
        [RelayCommand]
        private void ExportRecipe()
        {
            if (SelectedRecipe == null)
            {
                IoStatusMessage = "레시피를 먼저 선택하세요.";
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"{SelectedRecipe.Name}.csv"
            };
            if (dlg.ShowDialog() != true)
            {
                IoStatusMessage = string.Empty;
                return;
            }

            try
            {
                var recipe = SnapshotRecipe(SelectedRecipe);
                RecipeCsvSerializer.WriteFile(dlg.FileName, recipe);
                IoStatusMessage = $"내보냈습니다: {Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex)
            {
                IoStatusMessage = $"내보내기 실패: {ex.Message}";
            }
        }

        /// <summary>CSV 또는 JSON 파일에서 레시피를 가져온다. Id는 새로 발급해 기존 레시피를 덮어쓰지 않는다. 결과는 IoStatusMessage로 표시한다.</summary>
        [RelayCommand]
        private void ImportRecipe()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "레시피 파일 (*.csv;*.json)|*.csv;*.json|CSV (*.csv)|*.csv|JSON (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true)
            {
                IoStatusMessage = string.Empty;
                return;
            }

            try
            {
                Recipe? recipe;
                if (string.Equals(Path.GetExtension(dlg.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    recipe = RecipeCsvSerializer.ReadFile(dlg.FileName);
                }
                else
                {
                    var json = File.ReadAllText(dlg.FileName);
                    recipe = JsonSerializer.Deserialize<Recipe>(json);
                }

                if (recipe == null)
                {
                    IoStatusMessage = "파일을 레시피로 읽을 수 없습니다.";
                    return;
                }

                recipe.Id = Guid.NewGuid().ToString();
                _recipeRepository.SaveAsync(recipe).GetAwaiter().GetResult();

                var vm = FromDomain(recipe);
                Recipes.Add(vm);
                SelectRecipe(vm);
                IoStatusMessage = $"가져왔습니다: {recipe.Name}";
            }
            catch (Exception ex)
            {
                IoStatusMessage = $"가져오기 실패: {ex.Message}";
            }
        }

        /// <summary>레시피 깊은 복사본을 새 Id와 "(복사)" 이름으로 만든다. 스텝 목록까지 복제한다.</summary>
        public static Recipe CloneRecipe(Recipe source)
        {
            var copy = CapturePlanRecipeCopies.Clone(source);
            copy.Id = Guid.NewGuid().ToString();
            copy.Name = source.Name + " (복사)";
            return copy;
        }

        /// <summary>
        /// 편집 모델을 도메인 <see cref="Recipe"/>로 변환한다. CameraIndex/TargetPositionIndex가
        /// 0이면 NodeAssignment 문자열 파싱으로 폴백한다(구버전 데이터 호환).
        /// </summary>
        private static Recipe ToDomain(RecipeModel vm)
        {
            var r = new Recipe
            {
                Id = vm.Id,
                Name = vm.Name,
                Plan = vm.Plan is { } editorPlan ? CapturePlanRecipeCopies.ClonePlan(editorPlan) : null,
                PlanValidationError = vm.PlanValidationError,
                TemperatureRampMinutes = vm.RampMinutes,
                RecordOnTemperatureDelta = vm.RecordOnTemperatureDelta,
                OverwriteDuplicateShots = vm.OverwriteDuplicateShots,
                RecordOnHumidityDelta = vm.RecordOnHumidityDelta,
                RecordIntervalSeconds = vm.RecordIntervalSeconds
            };
            foreach (var s in vm.Steps)
            {
                var targets = s.CameraTargets
                    .Where(target => target.IsSelected)
                    .Select(target => new RecipeCameraTarget
                    {
                        AgentId = target.AgentId,
                        CameraIndex = target.CameraIndex,
                        TargetChamber = target.TargetChamber,
                        TargetBlackBody = target.TargetBlackBody
                    })
                    .ToList();
                r.Steps.Add(new RecipeStep
                {
                    StepId = s.StepId,
                    PlanCellKey = s.PlanCellKey,
                    UseCellCamera = s.UseCellCamera, CellPositionRole = s.CellPositionRole,
                    ShotNumberOffset = s.ShotNumberOffset,
                    CameraIndex = targets.FirstOrDefault()?.CameraIndex ?? (s.CameraIndex > 0 ? s.CameraIndex : ParseCameraIndex(s.NodeAssignment)),
                    TargetPositionIndex = s.TargetPositionIndex > 0 ? s.TargetPositionIndex : ParsePositionIndex(s.NodeAssignment),
                    Kind = s.Kind,
                    CameraOperation = s.CameraOperation,
                    CameraTargets = targets,
                    TargetBlackBodyTemperature = s.BlackbodyRef,
                    BlackBodyIndex = s.BlackBodyIndex,
                    TargetBlackBodyTemperature1 = s.BlackbodyRef1,
                    MotorMoveType = s.MotorMoveType,
                    WaitForStabilization = s.WaitForStabilization,
                    WaitForChamberStabilization = s.WaitForChamberStabilization,
                    UseSafetyTemperature = s.UseSafetyTemperature,
                    SafetyTempMin = s.SafetyTempMin,
                    SafetyTempMax = s.SafetyTempMax,
                    UseSafetyHumidity = s.UseSafetyHumidity,
                    SafetyHumidityMin = s.SafetyHumidityMin,
                    SafetyHumidityMax = s.SafetyHumidityMax,
                    ShotCount = s.ShotCount > 0 ? s.ShotCount : 1,
                    CaptureIntervalSeconds = s.CaptureIntervalSeconds,
                    CaptureDurationSeconds = s.CaptureDurationSeconds,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity,
                    TargetFanSpeedHz = s.TargetFanSpeedHz,
                    StabilizationToleranceC = s.StabilizationToleranceC,
                    StabilizationToleranceRh = s.StabilizationToleranceRh,
                    SoakMinutes = s.SoakMinutes,
                    BiasTargetLevel = s.BiasTargetLevel,
                    DisableHumidityControl = s.DisableHumidityControl,
                    WaitForCaptureResult = s.WaitForCaptureResult,
                    CaptureJoinTimeoutSeconds = s.CaptureJoinTimeoutSeconds,
                    WaitDurationSeconds = s.WaitHours * 3600 + s.WaitMinutes * 60 + s.WaitSeconds
                });
            }
            return r;
        }

        /// <summary>도메인 <see cref="Recipe"/>를 편집 모델로 변환하고 스텝 번호·표시 문자열을 만든다.</summary>
        private static RecipeModel FromDomain(Recipe r)
        {
            var vm = new RecipeModel
            {
                Id = r.Id,
                Name = r.Name,
                Plan = r.Plan is { } domainPlan ? CapturePlanRecipeCopies.ClonePlan(domainPlan) : null,
                PlanValidationError = r.Plan is { Entries: null } ? CapturePlanCompiler.OldFormatMessage : r.PlanValidationError,
                RampMinutes = r.TemperatureRampMinutes,
                RecordOnTemperatureDelta = r.RecordOnTemperatureDelta,
                OverwriteDuplicateShots = r.OverwriteDuplicateShots,
                RecordOnHumidityDelta = r.RecordOnHumidityDelta,
                RecordIntervalSeconds = r.RecordIntervalSeconds,
                LastModified = DateTime.Now.ToString("g")
            };
            int n = 1;
            foreach (var s in r.Steps)
            {
                var step = new RecipeStepModel
                {
                    StepNumber = n++,
                    StepId = s.StepId,
                    PlanCellKey = s.PlanCellKey,
                    UseCellCamera = s.UseCellCamera, CellPositionRole = s.CellPositionRole,
                    ShotNumberOffset = s.ShotNumberOffset,
                    NodeAssignment = $"Position {s.TargetPositionIndex:D2} -> CAM-{s.CameraIndex:D2}",
                    CameraIndex = s.CameraIndex,
                    TargetPositionIndex = s.TargetPositionIndex,
                    Kind = s.Kind,
                    CameraOperation = s.CameraOperation,
                    BlackbodyRef = s.TargetBlackBodyTemperature,
                    BlackBodyIndex = s.BlackBodyIndex,
                    BlackbodyRef1 = s.TargetBlackBodyTemperature1,
                    MotorMoveType = s.MotorMoveType,
                    IsPointLocked = s.MotorMoveType == MotorMoveType.Automatic,
                    WaitForStabilization = s.WaitForStabilization,
                    WaitForChamberStabilization = s.WaitForChamberStabilization,
                    UseSafetyTemperature = s.UseSafetyTemperature,
                    SafetyTempMin = s.SafetyTempMin,
                    SafetyTempMax = s.SafetyTempMax,
                    UseSafetyHumidity = s.UseSafetyHumidity,
                    SafetyHumidityMin = s.SafetyHumidityMin,
                    SafetyHumidityMax = s.SafetyHumidityMax,
                    ShotCount = s.ShotCount > 0 ? s.ShotCount : 1,
                    CaptureIntervalSeconds = s.CaptureIntervalSeconds,
                    CaptureDurationSeconds = s.CaptureDurationSeconds,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity,
                    TargetFanSpeedHz = s.TargetFanSpeedHz,
                    StabilizationToleranceC = s.StabilizationToleranceC,
                    StabilizationToleranceRh = s.StabilizationToleranceRh,
                    SoakMinutes = s.SoakMinutes,
                    BiasTargetLevel = s.BiasTargetLevel > 0
                        ? s.BiasTargetLevel
                        : CameraBiasDefaults.For(s.CameraOperation) ?? 0,
                    DisableHumidityControl = s.DisableHumidityControl,
                    WaitForCaptureResult = s.WaitForCaptureResult,
                    CaptureJoinTimeoutSeconds = s.CaptureJoinTimeoutSeconds,
                    WaitHours = s.WaitDurationSeconds / 3600,
                    WaitMinutes = (s.WaitDurationSeconds % 3600) / 60,
                    WaitSeconds = s.WaitDurationSeconds % 60
                };
                foreach (var target in s.CameraTargets)
                    step.CameraTargets.Add(new CameraTargetModel
                {
                    AgentId = target.AgentId,
                    CameraIndex = target.CameraIndex,
                    TargetChamber = target.TargetChamber,
                    TargetBlackBody = target.TargetBlackBody,
                    IsPersisted = true,
                    IsSelected = true
                });
                if (step.CameraTargets.Count == 0 && s.CameraIndex > 0)
                    step.CameraTargets.Add(new CameraTargetModel { CameraIndex = s.CameraIndex, IsSelected = true });
                vm.Steps.Add(step);
            }
            return vm;
        }

        /// <summary>"Position NN -> CAM-NN" 표시 문자열에서 카메라 인덱스를 파싱한다. 실패 시 1.</summary>
        private static int ParseCameraIndex(string s)
        {
            try { var p = s.Split(new[] { "-> CAM-" }, StringSplitOptions.None); if (p.Length > 1 && int.TryParse(p[1].Trim(), out int v)) return v; } catch { }
            return 1;
        }

        /// <summary>"Position NN -> CAM-NN" 표시 문자열에서 포지션 인덱스를 파싱한다. 실패 시 1.</summary>
        private static int ParsePositionIndex(string s)
        {
            try { var p = s.Replace("Position ", "").Split(new[] { " ->" }, StringSplitOptions.None); if (p.Length > 0 && int.TryParse(p[0].Trim(), out int v)) return v; } catch { }
            return 1;
        }
        [ObservableProperty] private RecipeStepModel? _selectedStep;
        [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _currentPreview;
        [ObservableProperty] private int _currentServoX;
        [ObservableProperty] private int _currentServoY;

        [ObservableProperty] private AgentCameraOption? _selectedPreviewCamera;

        public ObservableCollection<AgentCameraOption> OnlineAgentCameras { get; } = new();


        private void SubscribeCameraServices() => _ = SubscribeCameraServicesAsync();

        private async Task SubscribeCameraServicesAsync()
        {
            var nats = AppServices.NatsService;
            if (nats is null) return;
            try
            {
                await _subscriptions.AddAsync(ct => nats.SubscribeAgentStatusScopedAsync(OnAgentStatus, ct));
                await _subscriptions.AddAsync(ct => nats.SubscribeLiveFrameScopedAsync(OnLiveFrame, ct));
            }
            catch (Exception ex) { if (!_disposed) CameraCommandStatus = ex.Message; }
        }

        private void OnAgentStatus(AgentStatusMessage msg)
        {
            if (_disposed) return;
            _cameraDirectory.Note(msg);
            RunOnUi(() =>
            {
                if (_disposed) return;
                var online = _cameraDirectory.OnlineCameras;
                foreach (var option in OnlineAgentCameras.Where(option => !online.Any(camera =>
                    camera.AgentId == option.AgentId && camera.CameraIndex == option.CameraIndex)).ToArray())
                    OnlineAgentCameras.Remove(option);
                foreach (CameraIdentity camera in online)
                    if (!OnlineAgentCameras.Any(option => option.AgentId == camera.AgentId && option.CameraIndex == camera.CameraIndex))
                        OnlineAgentCameras.Add(new AgentCameraOption { AgentId = camera.AgentId, CameraIndex = camera.CameraIndex });
                if (SelectedPreviewCamera is not null && !online.Any(camera =>
                    camera.AgentId == SelectedPreviewCamera.AgentId && camera.CameraIndex == SelectedPreviewCamera.CameraIndex))
                    SelectedPreviewCamera = null;
                foreach (var step in Recipes.SelectMany(recipe => recipe.Steps))
                {
                    PruneStaleTargets(step.CameraTargets, OnlineAgentCameras);
                    SyncCameraTargets(step);
                }
            });
        }

        public static int PruneOfflineAgentCameras(
            IList<AgentCameraOption> online,
            IDictionary<(string AgentId, int CameraIndex), DateTime> lastSeen,
            DateTime utcNow)
        {
            int removed = 0;
            for (int i = online.Count - 1; i >= 0; i--)
            {
                var key = (online[i].AgentId, online[i].CameraIndex);
                if (!lastSeen.TryGetValue(key, out DateTime seen) || utcNow - seen > AgentDirectory.OnlineThreshold)
                {
                    online.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        public static int PruneStaleTargets(IList<CameraTargetModel> targets, IEnumerable<AgentCameraOption> online)
        {
            var keys = new HashSet<(string, int)>(online.Select(o => (o.AgentId, o.CameraIndex)));
            int removed = 0;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                CameraTargetModel target = targets[i];
                target.IsOnline = keys.Contains((target.AgentId, target.CameraIndex));
                if (target.AgentId.Length > 0 && !target.IsOnline && !target.IsSelected && !target.IsPersisted)
                {
                    targets.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        private void SyncCameraTargets(RecipeStepModel step)
        {
            foreach (var camera in OnlineAgentCameras)
            {
                var fallback = step.CameraTargets.FirstOrDefault(target => target.AgentId.Length == 0 && target.CameraIndex == camera.CameraIndex);
                if (fallback is not null)
                {
                    fallback.AgentId = camera.AgentId;
                    fallback.IsOnline = true;
                    continue;
                }
                var existing = step.CameraTargets.FirstOrDefault(target => target.AgentId == camera.AgentId && target.CameraIndex == camera.CameraIndex);
                if (existing is not null) existing.IsOnline = true;
                else step.CameraTargets.Add(new CameraTargetModel { AgentId = camera.AgentId, CameraIndex = camera.CameraIndex, IsOnline = true });
            }
        }

        /// <summary>선택된 미리보기 카메라의 프레임만 디코드해 표시한다. 나머지 카메라 프레임은 버린다.</summary>
        private void OnLiveFrame(LiveFrameMessage msg)
        {
            if (_disposed || msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;
            var sel = SelectedPreviewCamera;
            if (sel == null || msg.AgentId != sel.AgentId || msg.CameraIndex != sel.CameraIndex) return;

            var bmp = Decode(msg.ImageBytes);
            if (bmp is null) return;
            bmp = HeatingCameraSystem.Master.Services.LivePreviewColorMode.Apply(bmp);
            RunOnUi(() => { if (!_disposed) CurrentPreview = bmp; });
        }

        /// <summary>JPEG 바이트를 디코드한다. Freeze로 스레드 간 전달을 허용하며, 손상 데이터는 null을 반환한다.</summary>
        private static System.Windows.Media.Imaging.BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                using var ms = new MemoryStream(jpeg);
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
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

        partial void OnSelectedPreviewCameraChanged(AgentCameraOption? value)
        {
            CurrentPreview = null;
            _cameraCommandCancellation?.Cancel();
        }

        /// <summary>선택 스텝의 좌표(mm)로 서보를 이동시킨다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task GoToXyAsync()
        {
            if (SelectedStep != null && AppServices.PlcController != null)
            {
                await AppServices.PlcController.MoveToCoordinateAsync(SelectedStep.PositionX, SelectedStep.PositionY);
            }
        }

        /// <summary>현재 서보 X 위치(mm)를 선택 스텝의 X 좌표로 채운다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task UseCurrentXAsync()
        {
            if (AppServices.PlcController != null)
            {
                var st = await AppServices.PlcController.ReadStatusAsync();
                if (SelectedStep != null)
                {
                    SelectedStep.PositionX = st.ServoXPosition;
                }
            }
        }

        /// <summary>현재 서보 Y 위치(mm)를 선택 스텝의 Y 좌표로 채운다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task UseCurrentYAsync()
        {
            if (AppServices.PlcController != null)
            {
                var st = await AppServices.PlcController.ReadStatusAsync();
                if (SelectedStep != null)
                {
                    SelectedStep.PositionY = st.ServoYPosition;
                }
            }
        }

        /// <summary>X·Y축을 차례로 원점 복귀시킨다.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task HomeServoAsync()
        {
            if (AppServices.PlcController != null)
            {
                await AppServices.PlcController.HomeAsync(ServoAxis.X);
                await AppServices.PlcController.HomeAsync(ServoAxis.Y);
            }
        }

        /// <summary>선택된 미리보기 카메라에 카메라 제어 명령(Run/Stop/셔터)을 발행한다.</summary>
        private async System.Threading.Tasks.Task SendCameraOpAsync(string op)
        {
            var selected = SelectedPreviewCamera;
            if (_disposed || selected is null || IsCameraCommandBusy || AppServices.CameraCommands is not { } commands) return;
            if (AppServices.RecipeEngine?.IsRunning == true)
            {
                CameraCommandStatus = "레시피 실행 중에는 수동 카메라 명령을 사용할 수 없습니다.";
                return;
            }
            IsCameraCommandBusy = true;
            CameraCommandStatus = $"{selected.Label}: {op} 처리 중";
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_subscriptions.Token);
            _cameraCommandCancellation = cancellation;
            try
            {
                CameraControlAckMessage ack = await commands.SendAsync(selected.AgentId, selected.CameraIndex, op, cancellation.Token);
                if (!_disposed) CameraCommandStatus = $"{selected.Label}: {(ack.IsSuccess ? "✔" : "✘")} {ack.Op} {ack.Message}";
                if (!ack.IsSuccess) AlarmSink.Raise(AlarmSeverity.Error, "Camera", $"{selected.AgentId}: {ack.Message}");
            }
            catch (OperationCanceledException) { if (!_disposed) CameraCommandStatus = "명령이 취소되었습니다."; }
            catch (Exception ex)
            {
                if (!_disposed) CameraCommandStatus = $"{selected.Label}: {ex.Message}";
                AlarmSink.Raise(AlarmSeverity.Error, "Camera", $"{selected.AgentId}: {ex.Message}");
            }
            finally { _cameraCommandCancellation = null; IsCameraCommandBusy = false; }
        }

        [RelayCommand]
        private void CancelCameraCommand() => _cameraCommandCancellation?.Cancel();

        [RelayCommand] private System.Threading.Tasks.Task OpenShutterAsync() => SendCameraOpAsync(CameraControlOps.ShutterOpen);
        [RelayCommand] private System.Threading.Tasks.Task CloseShutterAsync() => SendCameraOpAsync(CameraControlOps.ShutterClose);
        [RelayCommand] private System.Threading.Tasks.Task StartCameraAsync() => SendCameraOpAsync(CameraControlOps.Run);
        [RelayCommand] private System.Threading.Tasks.Task StopCameraAsync() => SendCameraOpAsync(CameraControlOps.Stop);
        [RelayCommand] private System.Threading.Tasks.Task CaptureCameraAsync() => SendCameraOpAsync(CameraControlOps.Capture);
        [RelayCommand] private System.Threading.Tasks.Task RunNucAsync() => SendCameraOpAsync(CameraControlOps.Nuc);
        [RelayCommand] private System.Threading.Tasks.Task RunBiasLowAsync() => SendCameraOpAsync(CameraControlOps.BiasLow);
        [RelayCommand] private System.Threading.Tasks.Task RunBiasMidAsync() => SendCameraOpAsync(CameraControlOps.BiasMid);
        [RelayCommand] private System.Threading.Tasks.Task RunBiasHighAsync() => SendCameraOpAsync(CameraControlOps.BiasHigh);

        /// <summary>JOG 이동 시작(버튼 누름). View 코드비하인드가 직접 호출한다.</summary>
        public System.Threading.Tasks.Task StartJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, true) ?? System.Threading.Tasks.Task.CompletedTask;
        /// <summary>JOG 이동 정지(버튼 뗌). View 코드비하인드가 직접 호출한다.</summary>
        public System.Threading.Tasks.Task StopJog(ServoAxis axis, bool positive) => AppServices.PlcController?.JogAsync(axis, positive, false) ?? System.Threading.Tasks.Task.CompletedTask;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cameraCommandCancellation?.Cancel();
            _subscriptions.Dispose();
            if (PlanEditor is { } editor)
            {
                editor.PropertyChanged -= OnPlanEditorPropertyChanged;
                editor.Dispose();
            }
            PlanEditor = null;

        }
        public async ValueTask DisposeAsync() { Dispose(); await _subscriptions.DisposeAsync(); }

        private static void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
        }
    }
}
