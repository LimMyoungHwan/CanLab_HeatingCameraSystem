using HeatingCameraSystem.Core.Interfaces;
using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        [ObservableProperty] private double _biasTargetLevel;
        [ObservableProperty] private double _stabilizationToleranceC;
        [ObservableProperty] private double _stabilizationToleranceRh;
        [ObservableProperty] private int _soakMinutes;
        [ObservableProperty] private RecipeStepKind _kind = RecipeStepKind.LegacyCapture;
        [ObservableProperty] private MotorMoveType _motorMoveType = MotorMoveType.Manual;
        [ObservableProperty] private string _cameraOperation = CameraControlOps.Capture;

        public RecipeStepModel()
        {
            CameraTargets.CollectionChanged += OnCameraTargetsChanged;
        }

        public int CameraIndex { get; set; }
        public int TargetPositionIndex { get; set; }
        public bool ShowsAutomaticPoint => Kind == RecipeStepKind.MotorMove && MotorMoveType == MotorMoveType.Automatic;
        public bool ShowsManualCoordinates => Kind == RecipeStepKind.MotorMove && MotorMoveType == MotorMoveType.Manual;
        public ObservableCollection<CameraTargetModel> CameraTargets { get; } = new();
        public string SelectedCameraSummary => string.Join(", ", CameraTargets.Where(target => target.IsSelected).Select(target => target.Label).DefaultIfEmpty("카메라 선택"));
        public bool ShowsLegacyFields => Kind == RecipeStepKind.LegacyCapture;
        public bool ShowsMotorFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.MotorMove;
        public bool ShowsChamberFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.ChamberControl;
        public bool ShowsHumidityFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.HumidityControl;
        public bool ShowsCameraFields => Kind is RecipeStepKind.LegacyCapture or RecipeStepKind.CameraCommand;
        public bool ShowsCameraOperation => Kind == RecipeStepKind.CameraCommand;
        public bool IsCaptureOperation => CameraOperation == CameraControlOps.Capture;
        public bool IsBiasOperation => CameraOperation is CameraControlOps.BiasLow or CameraControlOps.BiasMid or CameraControlOps.BiasHigh;
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

        partial void OnCameraOperationChanged(string value)
        {
            OnPropertyChanged(nameof(IsCaptureOperation));
            OnPropertyChanged(nameof(IsBiasOperation));
        }

        partial void OnMotorMoveTypeChanged(MotorMoveType value)
        {
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

    public partial class CameraTargetModel : ObservableObject
    {
        [ObservableProperty] private string _agentId = string.Empty;
        [ObservableProperty] private int _cameraIndex;
        [ObservableProperty] private bool _isSelected;
        public string Label => $"{AgentId} (CAM-{CameraIndex:D2})";

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

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _lastModified = string.Empty;
        [ObservableProperty] private int _rampMinutes;
        [ObservableProperty] private float _recordOnTemperatureDelta;
        [ObservableProperty] private float _recordOnHumidityDelta;
        [ObservableProperty] private int _recordIntervalSeconds;
        [ObservableProperty] private bool _isSequentialMode = true;

        public ObservableCollection<RecipeStepModel> Steps { get; } = new();
    }

    /// <summary>
    /// 레시피 편집 화면. 레시피 CRUD·복사·JSON 가져오기/내보내기, 스텝 편집(추가·삭제·순서 이동),
    /// 그리고 좌표 잡기를 위한 서보 이동·JOG와 카메라 라이브 미리보기를 담당한다.
    /// NATS 콜백은 백그라운드 스레드로 오므로 UI 갱신은 Dispatcher로 마샬링한다.
    /// </summary>
    public partial class RecipeEditorViewModel : ObservableObject, IDisposable
    {
        public ObservableCollection<RecipeModel> Recipes { get; } = new ObservableCollection<RecipeModel>();
        public RecipeStepKindOption[] StepKindOptions { get; } =
        {
            new() { Value = RecipeStepKind.MotorMove, Label = "PLC 모터 이동" },
            new() { Value = RecipeStepKind.ChamberControl, Label = "PLC 온도 설정" },
            new() { Value = RecipeStepKind.HumidityControl, Label = "PLC 습도 설정" },
            new() { Value = RecipeStepKind.CameraCommand, Label = "카메라 명령" },
            new() { Value = RecipeStepKind.BlackBodyControl, Label = "블랙바디 온도 제어" }
        };
        public CameraOperationOption[] CameraOperationOptions { get; } =
        {
            new() { Value = CameraControlOps.Capture, Label = "캡처" },
            new() { Value = CameraControlOps.ShutterOpen, Label = "셔터 열기" },
            new() { Value = CameraControlOps.ShutterClose, Label = "셔터 닫기" },
            new() { Value = CameraControlOps.BiasLow, Label = "BIAS LOW" },
            new() { Value = CameraControlOps.BiasMid, Label = "BIAS MID" },
            new() { Value = CameraControlOps.BiasHigh, Label = "BIAS HIGH" },
            new() { Value = CameraControlOps.Nuc, Label = "NUC 실행" },
            new() { Value = CameraControlOps.Run, Label = "카메라 RUN" },
            new() { Value = CameraControlOps.Stop, Label = "카메라 STOP" },
            new() { Value = CameraControlOps.SaveConfig, Label = "설정 저장" },
            new() { Value = CameraControlOps.RefreshInfo, Label = "정보 갱신" }
        };

        public int[] BlackBodyIndexOptions { get; } = { 0, 1 };
        public MotorMoveTypeOption[] MotorMoveTypeOptions { get; } =
        {
            new() { Value = MotorMoveType.Manual, Label = "수동" },
            new() { Value = MotorMoveType.Automatic, Label = "자동" }
        };
        public int[] ServoPointOptions { get; } = Enumerable.Range(1, 20).ToArray();

        [ObservableProperty]
        private RecipeModel? _selectedRecipe;

        public RecipeEditorViewModel()
        {
            SubscribeCameraServices();

            foreach (var r in AppServices.RecipeRepo.GetAllAsync().GetAwaiter().GetResult())
                Recipes.Add(FromDomain(r));

            if (Recipes.Count > 0)
                SelectRecipe(Recipes[0]);
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
            AppServices.RecipeRepo.SaveAsync(ToDomain(vm)).GetAwaiter().GetResult();
            SelectRecipe(vm);
            RecipeAdded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>선택 레시피를 새 Id로 복제해 저장·선택한다. 이름에는 "(복사)"가 붙는다.</summary>
        [RelayCommand]
        private void CopyRecipe()
        {
            if (SelectedRecipe == null) return;

            var clone = CloneRecipe(ToDomain(SelectedRecipe));
            AppServices.RecipeRepo.SaveAsync(clone).GetAwaiter().GetResult();

            var vm = FromDomain(clone);
            Recipes.Add(vm);
            SelectRecipe(vm);
        }

        /// <summary>선택 레시피를 저장하고 수정 시각을 갱신한다.</summary>
        [RelayCommand]
        private void SaveRecipe()
        {
            if (SelectedRecipe == null) return;
            SelectedRecipe.LastModified = DateTime.Now.ToString("g");
            AppServices.RecipeRepo.SaveAsync(ToDomain(SelectedRecipe)).GetAwaiter().GetResult();
        }

        /// <summary>레시피를 삭제한다. 선택 중이던 레시피였다면 남은 첫 레시피를 선택한다.</summary>
        [RelayCommand]
        private void DeleteRecipe(RecipeModel recipe)
        {
            if (recipe == null) return;
            AppServices.RecipeRepo.DeleteAsync(recipe.Id).GetAwaiter().GetResult();
            Recipes.Remove(recipe);
            if (SelectedRecipe == recipe)
                SelectedRecipe = Recipes.FirstOrDefault();
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
            step.CameraTargets.Add(new CameraTargetModel { CameraIndex = n, IsSelected = true });
            if (kind == RecipeStepKind.BlackBodyControl)
            {
                step.BlackbodyRef = 25.0f;
                step.BlackBodyIndex = 0;
                step.WaitForStabilization = true;
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

        /// <summary>선택 레시피를 JSON 파일로 내보낸다.</summary>
        [RelayCommand]
        private void ExportRecipe()
        {
            if (SelectedRecipe == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{SelectedRecipe.Name}.json"
            };
            if (dlg.ShowDialog() != true) return;

            var recipe = ToDomain(SelectedRecipe);
            var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
        }

        /// <summary>JSON 파일에서 레시피를 가져온다. Id는 새로 발급해 기존 레시피를 덮어쓰지 않는다.</summary>
        [RelayCommand]
        private void ImportRecipe()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var recipe = JsonSerializer.Deserialize<Recipe>(json);
                if (recipe == null) return;

                recipe.Id = Guid.NewGuid().ToString();
                AppServices.RecipeRepo.SaveAsync(recipe).GetAwaiter().GetResult();

                var vm = FromDomain(recipe);
                Recipes.Add(vm);
                SelectRecipe(vm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] Import failed: {ex.Message}");
            }
        }

        /// <summary>촬영 방식 토글. "Sequential"이면 순차 모드, 그 외는 동시 모드다.</summary>
        [RelayCommand]
        private void SetCaptureMode(string mode)
        {
            if (SelectedRecipe != null)
                SelectedRecipe.IsSequentialMode = mode == "Sequential";
        }

        /// <summary>레시피 깊은 복사본을 새 Id와 "(복사)" 이름으로 만든다. 스텝 목록까지 복제한다.</summary>
        public static Recipe CloneRecipe(Recipe source)
        {
            return new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = source.Name + " (복사)",
                TemperatureRampMinutes = source.TemperatureRampMinutes,
                RecordOnTemperatureDelta = source.RecordOnTemperatureDelta,
                RecordOnHumidityDelta = source.RecordOnHumidityDelta,
                RecordIntervalSeconds = source.RecordIntervalSeconds,
                Steps = source.Steps.Select(s => new RecipeStep
                {
                    StepId = s.StepId,
                    CameraIndex = s.CameraIndex,
                    CameraAlias = s.CameraAlias,
                    Kind = s.Kind,
                    CameraOperation = s.CameraOperation,
                    CameraTargets = s.CameraTargets.Select(target => new RecipeCameraTarget
                    {
                        AgentId = target.AgentId,
                        CameraIndex = target.CameraIndex
                    }).ToList(),
                    TargetPositionIndex = s.TargetPositionIndex,
                    TargetBlackBodyTemperature = s.TargetBlackBodyTemperature,
                    BlackBodyIndex = s.BlackBodyIndex,
                    TargetBlackBodyTemperature1 = s.TargetBlackBodyTemperature1,
                    MotorMoveType = s.MotorMoveType,
                    WaitForStabilization = s.WaitForStabilization,
                    WaitForChamberStabilization = s.WaitForChamberStabilization,
                    UseSafetyTemperature = s.UseSafetyTemperature,
                    SafetyTempMin = s.SafetyTempMin,
                    SafetyTempMax = s.SafetyTempMax,
                    UseSafetyHumidity = s.UseSafetyHumidity,
                    SafetyHumidityMin = s.SafetyHumidityMin,
                    SafetyHumidityMax = s.SafetyHumidityMax,
                    ShotCount = s.ShotCount,
                    CaptureIntervalSeconds = s.CaptureIntervalSeconds,
                    CaptureDurationSeconds = s.CaptureDurationSeconds,
                    PositionX = s.PositionX,
                    PositionY = s.PositionY,
                    TargetChamberTemperature = s.TargetChamberTemperature,
                    TargetChamberHumidity = s.TargetChamberHumidity,
                    StabilizationToleranceC = s.StabilizationToleranceC,
                    StabilizationToleranceRh = s.StabilizationToleranceRh,
                    SoakMinutes = s.SoakMinutes,
                    BiasTargetLevel = s.BiasTargetLevel
                }).ToList()
            };
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
                TemperatureRampMinutes = vm.RampMinutes,
                RecordOnTemperatureDelta = vm.RecordOnTemperatureDelta,
                RecordOnHumidityDelta = vm.RecordOnHumidityDelta,
                RecordIntervalSeconds = vm.RecordIntervalSeconds
            };
            foreach (var s in vm.Steps)
            {
                var targets = s.CameraTargets
                    .Where(target => target.IsSelected)
                    .Select(target => new RecipeCameraTarget { AgentId = target.AgentId, CameraIndex = target.CameraIndex })
                    .ToList();
                r.Steps.Add(new RecipeStep
                {
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
                    StabilizationToleranceC = s.StabilizationToleranceC,
                    StabilizationToleranceRh = s.StabilizationToleranceRh,
                    SoakMinutes = s.SoakMinutes,
                    BiasTargetLevel = s.BiasTargetLevel
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
                RampMinutes = r.TemperatureRampMinutes,
                RecordOnTemperatureDelta = r.RecordOnTemperatureDelta,
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
                    NodeAssignment = $"Position {s.TargetPositionIndex:D2} -> CAM-{s.CameraIndex:D2}",
                    CameraIndex = s.CameraIndex,
                    TargetPositionIndex = s.TargetPositionIndex,
                    Kind = s.Kind,
                    CameraOperation = s.CameraOperation,
                    BlackbodyRef = s.TargetBlackBodyTemperature,
                    BlackBodyIndex = s.BlackBodyIndex,
                    BlackbodyRef1 = s.TargetBlackBodyTemperature1,
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
                    StabilizationToleranceC = s.StabilizationToleranceC,
                    StabilizationToleranceRh = s.StabilizationToleranceRh,
                    SoakMinutes = s.SoakMinutes,
                    BiasTargetLevel = s.BiasTargetLevel
                };
                foreach (var target in s.CameraTargets)
                    step.CameraTargets.Add(new CameraTargetModel { AgentId = target.AgentId, CameraIndex = target.CameraIndex, IsSelected = true });
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

        private void SubscribeCameraServices()
        {
            var nats = AppServices.NatsService;
            if (nats == null) return;
            try
            {
                nats.SubscribeAgentStatusAsync(OnAgentStatus);
                nats.SubscribeLiveFrameAsync(OnLiveFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] NATS subscribe failed: {ex.Message}");
            }
        }

        private void OnAgentStatus(AgentStatusMessage msg)
        {
            if (string.IsNullOrEmpty(msg.AgentId)) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!OnlineAgentCameras.Any(a => a.AgentId == msg.AgentId && a.CameraIndex == msg.CameraIndex))
                    OnlineAgentCameras.Add(new AgentCameraOption { AgentId = msg.AgentId, CameraIndex = msg.CameraIndex });
                foreach (var step in Recipes.SelectMany(recipe => recipe.Steps))
                    SyncCameraTargets(step);
            });
        }

        private void SyncCameraTargets(RecipeStepModel step)
        {
            foreach (var camera in OnlineAgentCameras)
            {
                var fallback = step.CameraTargets.FirstOrDefault(target => target.AgentId.Length == 0 && target.CameraIndex == camera.CameraIndex);
                if (fallback is not null)
                {
                    fallback.AgentId = camera.AgentId;
                    continue;
                }
                if (!step.CameraTargets.Any(target => target.AgentId == camera.AgentId && target.CameraIndex == camera.CameraIndex))
                    step.CameraTargets.Add(new CameraTargetModel { AgentId = camera.AgentId, CameraIndex = camera.CameraIndex });
            }
        }

        /// <summary>선택된 미리보기 카메라의 프레임만 디코드해 표시한다. 나머지 카메라 프레임은 버린다.</summary>
        private void OnLiveFrame(LiveFrameMessage msg)
        {
            if (msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;
            var sel = SelectedPreviewCamera;
            if (sel == null || msg.AgentId != sel.AgentId || msg.CameraIndex != sel.CameraIndex) return;

            var bmp = Decode(msg.ImageBytes);
            if (bmp is null) return;
            bmp = HeatingCameraSystem.Master.Services.LivePreviewColorMode.Apply(bmp);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => CurrentPreview = bmp));
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

        partial void OnSelectedPreviewCameraChanged(AgentCameraOption? value) => CurrentPreview = null;

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
            var sel = SelectedPreviewCamera;
            if (sel == null || AppServices.NatsService == null) return;
            try
            {
                await AppServices.NatsService.PublishCameraControlAsync(new CameraControlMessage
                {
                    AgentId = sel.AgentId,
                    CameraIndex = sel.CameraIndex,
                    Op = op,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeEditor] camera op publish failed: {ex.Message}");
            }
        }

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
            // ponytail: NatsCommunicationService에는 구독 해제 API가 없다(fire-and-forget 루프).
            // ManualControlViewModel과 같은 상황이므로 여기서 해제할 것이 없다.
        }
    }
}
