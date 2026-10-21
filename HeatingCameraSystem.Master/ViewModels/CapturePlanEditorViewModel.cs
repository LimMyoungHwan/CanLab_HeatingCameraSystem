using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels;

public sealed class CapturePlanEditorViewModel : ObservableObject, IDisposable
{
    private readonly Recipe _recipe;
    private readonly ICameraPositionRepository? _positions;
    private readonly IRecipeRepository? _recipes;
    private readonly Func<IReadOnlyList<CameraIdentity>> _online;
    private readonly DispatcherTimer _timer;
    private CapturePlanEntryEditor? _selectedEntry;
    private string _validationMessage = string.Empty;
    private bool _isValid;
    private bool _disposed;
    private string? _positionError;
    public CapturePlanEditorViewModel(Recipe recipe, ICameraPositionRepository? repository = null, IRecipeRepository? recipes = null,
        Func<IReadOnlyList<CameraIdentity>>? online = null)
    {
        _recipe = CapturePlanRecipeCopies.Clone(recipe);
        _recipe.Plan ??= new CapturePlan { Entries = new() };
        _positions = repository ?? AppServices.CameraPositions;
        _recipes = recipes ?? AppServices.RecipeRepo;
        _online = online ?? (() => AppServices.AgentDirectory?.OnlineCameras ?? Array.Empty<CameraIdentity>());
        foreach (var entry in Plan.Entries ?? new()) Entries.Add(new(entry, Changed));
        _selectedEntry = Entries.FirstOrDefault();
        AddEntryCommand = new RelayCommand(() =>
        {
            if (Plan.Entries == null) return;
            var entry = new CapturePlanEntryEditor(new CapturePlanEntry { TemplateRecipeId = Templates.FirstOrDefault()?.Id ?? string.Empty }, Changed);
            Entries.Add(entry); SelectedEntry = entry; Changed();
        });
        DeleteEntryCommand = new RelayCommand(() => { if (SelectedEntry is not { } selected) return; Entries.Remove(selected); SelectedEntry = Entries.FirstOrDefault(); Changed(); });
        MoveEntryUpCommand = new RelayCommand(() => Move(-1));
        MoveEntryDownCommand = new RelayCommand(() => Move(1));
        RefreshCommand = new AsyncRelayCommand(LoadPositionsAsync);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += Tick; _timer.Start();
        RefreshTemplates(); RefreshCameras(); TryCompile(out _);
    }
    public CapturePlan Plan => _recipe.Plan ?? throw new InvalidOperationException("촬영 계획이 없습니다.");
    public bool OverwriteDuplicateShots
    {
        get => _recipe.OverwriteDuplicateShots;
        set
        {
            if (_recipe.OverwriteDuplicateShots == value) return;
            _recipe.OverwriteDuplicateShots = value;
            OnPropertyChanged();
            Changed();
        }
    }
    public ObservableCollection<CapturePlanEntryEditor> Entries { get; } = new();
    public ObservableCollection<Recipe> Templates { get; } = new();
    public ObservableCollection<GeneratedPlanStep> GeneratedSteps { get; } = new();
    public IReadOnlyList<CameraPositionEntry> PositionSnapshot { get; private set; } = Array.Empty<CameraPositionEntry>();
    public IReadOnlyList<Recipe> TemplateSnapshot { get; private set; } = Array.Empty<Recipe>();
    public CapturePlanEntryEditor? SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public bool IsValid { get => _isValid; private set => SetProperty(ref _isValid, value); }
    public IRelayCommand AddEntryCommand { get; }
    public IRelayCommand DeleteEntryCommand { get; }
    public IRelayCommand MoveEntryUpCommand { get; }
    public IRelayCommand MoveEntryDownCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    private void Move(int offset)
    {
        if (SelectedEntry is not { } entry) return;
        int index = Entries.IndexOf(entry), next = index + offset;
        if (next >= 0 && next < Entries.Count) { Entries.Move(index, next); Changed(); }
    }
    private void RefreshTemplates()
    {
        TemplateSnapshot = _recipes?.GetAllAsync().GetAwaiter().GetResult().Select(CapturePlanRecipeCopies.Clone).ToArray() ?? Array.Empty<Recipe>();
        var available = TemplateSnapshot.Where(r => r.Plan == null).ToList();
        foreach (var entry in Entries.Where(e => !string.IsNullOrWhiteSpace(e.TemplateRecipeId)))
            if (!available.Any(t => t.Id == entry.TemplateRecipeId))
                available.Add(new Recipe { Id = entry.TemplateRecipeId, Name = "연결된 레시피 없음 (실행 불가)" });
        // Keep selected IDs present while refreshing ItemsSource; a transient empty list
        // must not write null through ComboBox.SelectedValue into the stored live link.
        foreach (var template in available)
            if (!Templates.Any(t => t.Id == template.Id)) Templates.Add(template);
        foreach (var old in Templates.Where(t => !available.Any(a => a.Id == t.Id)).ToArray()) Templates.Remove(old);
    }
    public void RefreshCameras()
    {
        var online = _online();
        foreach (var entry in Entries) entry.RefreshCameras(online);
    }
    private void Tick(object? sender, EventArgs args) => RefreshCameras();
    public async Task LoadPositionsAsync()
    {
        try
        {
            PositionSnapshot = _positions == null ? Array.Empty<CameraPositionEntry>() : await _positions.GetAllAsync();
            if (_disposed) return;
            _positionError = null;
            RefreshTemplates(); RefreshCameras(); TryCompile(out _);
        }
        catch (Exception error)
        {
            PositionSnapshot = Array.Empty<CameraPositionEntry>();
            GeneratedSteps.Clear(); _positionError = error.Message;
            IsValid = false; ValidationMessage = "실행 불가: " + error.Message;
        }
    }
    private void Changed()
    {
        if (_disposed) return;
        if (Plan.Entries != null) Plan.Entries = Entries.Select(e => e.Model).ToList();
        RefreshCameras(); TryCompile(out _);
    }
    public bool TryCompile(out List<RecipeStep> steps)
    {
        if (Plan.Entries != null) Plan.Entries = Entries.Select(e => e.Model).ToList();
        try
        {
            if (_positionError != null) throw new CapturePlanValidationException(_positionError);
            // Resolve the live links again; cached generated steps are never an execution source.
            TemplateSnapshot = _recipes?.GetAllAsync().GetAwaiter().GetResult().Select(CapturePlanRecipeCopies.Clone).ToArray() ?? Array.Empty<Recipe>();
            steps = CapturePlanCompiler.Compile(_recipe, PositionSnapshot, TemplateSnapshot);
            GeneratedSteps.Clear();
            for (int i = 0; i < steps.Count; i++) GeneratedSteps.Add(new(i + 1, steps[i]));
            IsValid = true;
            ValidationMessage = $"실행 가능 · {Entries.Sum(e => e.Model.Cameras.Count)}개 카메라 셀 · {steps.Count}스텝 · 항목 순서 → 선택 카메라 순서";
            return true;
        }
        catch (ArgumentException error)
        {
            steps = new(); GeneratedSteps.Clear(); IsValid = false;
            ValidationMessage = "실행 불가: " + error.Message; return false;
        }
    }
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= Tick; }
}

public sealed class CapturePlanEntryEditor : ObservableObject
{
    private readonly Action _changed;
    public CapturePlanEntryEditor(CapturePlanEntry model, Action changed) { Model = model; _changed = changed; }
    public CapturePlanEntry Model { get; }
    public string TemplateRecipeId { get => Model.TemplateRecipeId; set { if (Model.TemplateRecipeId == value) return; Model.TemplateRecipeId = value ?? string.Empty; OnPropertyChanged(); _changed(); } }
    public ObservableCollection<PlanCameraChoice> Cameras { get; } = new();
    public string Summary => $"선택 카메라 {Model.Cameras.Count}대";
    public void RefreshCameras(IReadOnlyList<CameraIdentity> online)
    {
        var available = online.Concat(Model.Cameras).GroupBy(c => c.Key).Select(g => g.First()).ToArray();
        foreach (var old in Cameras.Where(c => !available.Any(a => a.Key == c.Camera.Key)).ToArray()) Cameras.Remove(old);
        foreach (var camera in available)
        {
            var choice = Cameras.FirstOrDefault(c => c.Camera.Key == camera.Key);
            if (choice == null)
            {
                choice = new PlanCameraChoice(camera.Copy(), Model.Cameras.Any(c => c.Key == camera.Key), selected =>
                {
                    if (selected && !Model.Cameras.Any(c => c.Key == camera.Key)) Model.Cameras.Add(camera.Copy());
                    if (!selected) Model.Cameras.RemoveAll(c => c.Key == camera.Key);
                    OnPropertyChanged(nameof(Summary)); _changed();
                });
                Cameras.Add(choice);
            }
            choice.IsOnline = online.Any(c => c.Key == camera.Key);
        }
    }
}

public sealed class PlanCameraChoice : ObservableObject
{
    private readonly Action<bool> _changed;
    private bool _selected, _online;
    public PlanCameraChoice(CameraIdentity camera, bool selected, Action<bool> changed) { Camera = camera; _selected = selected; _changed = changed; }
    public CameraIdentity Camera { get; }
    public bool IsSelected { get => _selected; set { if (SetProperty(ref _selected, value)) _changed(value); } }
    public bool IsOnline { get => _online; set { if (SetProperty(ref _online, value)) OnPropertyChanged(nameof(Label)); } }
    public string Label => Camera.Label + (IsOnline ? " · 온라인" : " · 오프라인");
}

public sealed class GeneratedPlanStep
{
    public GeneratedPlanStep(int number, RecipeStep step) { Number = number; Step = step; }
    public int Number { get; }
    public RecipeStep Step { get; }
    public string Label => $"{Number}. " + (Step.Kind switch
    {
        RecipeStepKind.ChamberControl => $"챔버 {Step.TargetChamberTemperature:g}℃",
        RecipeStepKind.BlackBodyControl => $"흑체 {Step.TargetBlackBodyTemperature:g}/{Step.TargetBlackBodyTemperature1:g}℃",
        RecipeStepKind.MotorMove => $"이동 · {Step.CellPositionRole} · " + (Step.MotorMoveType == MotorMoveType.Automatic ? $"Point {Step.TargetPositionIndex}" : $"({Step.PositionX:g}, {Step.PositionY:g}) mm"),
        RecipeStepKind.Wait => $"대기 {CapturePlanLabels.Duration(Step.WaitDurationSeconds)}",
        RecipeStepKind.CameraCommand => $"{CapturePlanLabels.Operation(Step.CameraOperation)} · {Step.CameraTargets.FirstOrDefault()?.AgentId} (CAM-{Step.CameraIndex:D2}) · {Step.CameraTargets.FirstOrDefault()?.TargetChamber}/{Step.CameraTargets.FirstOrDefault()?.TargetBlackBody}" +
            (Step.CameraOperation == CameraControlOps.Capture ? $" · {Step.ShotCount}장 · 시작번호 {Step.ShotNumberOffset}" : string.Empty),
        RecipeStepKind.HumidityControl => $"습도 {Step.TargetChamberHumidity:g}%",
        RecipeStepKind.FanControl => $"팬 {Step.TargetFanSpeedHz:g}Hz",
        RecipeStepKind.CaptureJoin => "촬영 결과 대기",
        _ => Step.Kind.ToString()
    });
}
