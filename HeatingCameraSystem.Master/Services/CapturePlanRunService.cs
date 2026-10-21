using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services;

public sealed class PreparedCapturePlanRun
{
    public Recipe Recipe { get; init; } = new();
    public Recipe FullRecipe { get; init; } = new();
    public CapturePlanRunRecord Run { get; init; } = new();
    public string Warning { get; init; } = string.Empty;
    public int CellCount => Run.Cells.Count(c => c.State != CapturePlanCellState.Excluded);
}

public sealed class CapturePlanRunService
{
    private readonly ICapturePlanRunRepository _repository;
    private readonly ICameraPositionRepository _positions;
    private readonly IRecipeRepository _recipes;
    private readonly Func<IReadOnlyList<CameraIdentity>> _online;
    public event EventHandler? BoardChanged;
    public CapturePlanRunService(ICapturePlanRunRepository repository, ICameraPositionRepository positions, IRecipeRepository recipes,
        Func<IReadOnlyList<CameraIdentity>>? online = null)
    {
        _repository = repository; _positions = positions; _recipes = recipes;
        _online = online ?? (() => AppServices.AgentDirectory?.OnlineCameras ?? Array.Empty<CameraIdentity>());
    }
    public async Task<PreparedCapturePlanRun> PrepareAsync(Recipe recipe, CapturePlanRunMode mode, CancellationToken token = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        token.ThrowIfCancellationRequested();
        var full = CapturePlanRecipeCopies.Clone(recipe);
        var plan = full.Plan ?? throw new CapturePlanValidationException("촬영 계획이 없습니다.");
        var templates = (await _recipes.GetAllAsync()).Select(CapturePlanRecipeCopies.Clone).ToArray();
        var positions = await _positions.GetAllAsync();
        token.ThrowIfCancellationRequested();
        full.Steps = CapturePlanCompiler.Compile(full, positions, templates);
        full.PlanValidationError = null;
        var chain = (await _repository.GetLatestChainAsync(recipe.Id)).Where(r => r.SchemaVersion == 2).ToArray();
        var aggregate = Reduce(chain);
        var previous = chain.LastOrDefault();
        string fingerprint = CapturePlanCompiler.TemplateFingerprint(plan, templates);
        string warning = previous != null && previous.TemplateFingerprint != fingerprint ? "템플릿이 이전 시도 이후 변경됨 · 현재 템플릿으로 재컴파일합니다. 완료 셀은 유지하고 선택 셀은 처음부터 실행합니다." : string.Empty;
        var run = new CapturePlanRunRecord { SchemaVersion = 2, RecipeId = recipe.Id, RecipeName = recipe.Name, TemplateFingerprint = fingerprint, StartedAt = DateTime.UtcNow, Outcome = CapturePlanRunOutcome.Running };
        run.RootRunId = mode == CapturePlanRunMode.New ? run.Id : previous?.RootRunId ?? run.Id;
        run.ParentRunId = mode == CapturePlanRunMode.New ? null : previous?.Id;
        if (previous != null && run.StartedAt <= previous.StartedAt.AddMilliseconds(1)) run.StartedAt = previous.StartedAt.AddMilliseconds(1);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in plan.Entries ?? new())
        foreach (var camera in entry.Cameras)
        {
            string key = CapturePlanCompiler.CellKey(entry.Key, camera);
            aggregate.TryGetValue(key, out var old);
            bool selected = mode switch { CapturePlanRunMode.New => true, CapturePlanRunMode.Remaining => old?.State != CapturePlanCellState.Done, _ => old?.State == CapturePlanCellState.Failed };
            if (!selected) excluded.Add(key);
            run.Cells.Add(new CapturePlanRunCell
            {
                CellKey = key, EntryKey = entry.Key, EntryLabel = CapturePlanLabels.Entry(plan, entry.Key, templates), Camera = camera.Copy(),
                State = selected ? CapturePlanCellState.Pending : CapturePlanCellState.Excluded,
                Steps = full.Steps.Where(s => s.PlanCellKey == key).Select(Receipt).ToList()
            });
        }
        var execution = CapturePlanRecipeCopies.Clone(full);
        if (mode != CapturePlanRunMode.New) execution.Steps = CapturePlanCompiler.CompileRemaining(full, positions, templates, excluded);
        return new PreparedCapturePlanRun { Recipe = execution, FullRecipe = full, Run = run, Warning = warning };
    }
    private static CapturePlanStepReceipt Receipt(RecipeStep step) => new()
    {
        StepId = step.StepId, Kind = step.Kind, Operation = step.CameraOperation,
        Role = step.Kind == RecipeStepKind.CameraCommand ? step.CameraTargets.FirstOrDefault()?.TargetBlackBody : step.CellPositionRole
    };
    public async Task<CapturePlanBoard> LoadBoardAsync(Recipe recipe)
    {
        var chain = (await _repository.GetLatestChainAsync(recipe.Id)).Where(r => r.SchemaVersion == 2).ToArray();
        var receipts = Reduce(chain);
        var board = new CapturePlanBoard { RootRunId = chain.FirstOrDefault()?.RootRunId };
        if (recipe.Plan is not { } plan) return board;
        if (plan.Entries == null) { board.Warning = CapturePlanCompiler.OldFormatMessage; return board; }
        var templates = (await _recipes.GetAllAsync()).ToArray();
        if (chain.LastOrDefault() is { } previous && previous.TemplateFingerprint != CapturePlanCompiler.TemplateFingerprint(plan, templates))
            board.Warning = "템플릿이 이전 시도 이후 변경됨 · 완료 셀은 이전 시도의 결과입니다. 재개/재촬영은 현재 템플릿으로 실행합니다.";
        var online = _online();
        foreach (var entry in plan.Entries)
        {
            var section = new CapturePlanBoardEntry { Label = CapturePlanLabels.Entry(plan, entry.Key, templates) };
            foreach (var camera in entry.Cameras)
            {
                string key = CapturePlanCompiler.CellKey(entry.Key, camera);
                receipts.TryGetValue(key, out var receipt);
                var expected = templates.FirstOrDefault(t => t.Id == entry.TemplateRecipeId)?.Steps.Select(source =>
                {
                    var step = Receipt(source);
                    step.StepId = CapturePlanCompiler.StepId(recipe.Id, entry.Key, camera, source.StepId);
                    return step;
                }).ToList();
                section.Cells.Add(new CapturePlanBoardCell
                {
                    CellKey = key, Camera = camera.Copy(), IsOnline = online.Any(c => c.Key == camera.Key),
                    State = receipt?.State ?? CapturePlanCellState.Pending, Reason = receipt?.Reason,
                    Steps = receipt?.Steps ?? expected ?? new()
                });
            }
            board.Entries.Add(section);
        }
        return board;
    }

    internal static void ApplyStep(CapturePlanRunCell cell, RecipeStepExecution step)
    {
        var receipt = cell.Steps.FirstOrDefault(s => s.StepId == step.StepId);
        if (receipt == null) return;
        receipt.Outcome = step.Outcome; receipt.Reason = step.Reason;
        if (step.Outcome == RecipeStepOutcome.Failed) { cell.State = CapturePlanCellState.Failed; cell.Reason = step.Reason ?? "스텝 실행에 실패했습니다."; }
        else if (step.Outcome == RecipeStepOutcome.Skipped && cell.State != CapturePlanCellState.Failed) { cell.State = CapturePlanCellState.Skipped; cell.Reason = step.Reason ?? "카메라를 건너뛰었습니다."; }
        else if (cell.State is not CapturePlanCellState.Failed and not CapturePlanCellState.Skipped)
            cell.State = cell.Steps.All(s => s.Outcome == RecipeStepOutcome.Succeeded) ? CapturePlanCellState.Done : CapturePlanCellState.Running;
    }

    public async Task ExecuteAsync(PreparedCapturePlanRun prepared, RecipeEngine engine, CancellationToken token,
        IProgress<RecipeProgress>? progress = null, Func<CancellationToken, Task<bool>>? waitForResumeAsync = null)
    {
        var run = prepared.Run; var gate = new object();
        void Persist() { _repository.SaveAsync(run).GetAwaiter().GetResult(); BoardChanged?.Invoke(this, EventArgs.Empty); }
        void OnStep(object? sender, RecipeStepExecution step)
        {
            if (step.RunId != run.Id || step.PlanCellKey == null) return;
            lock (gate)
            {
                var cell = run.Cells.FirstOrDefault(c => c.CellKey == step.PlanCellKey && c.State != CapturePlanCellState.Excluded);
                if (cell == null) return;
                ApplyStep(cell, step);
                if (cell.Reason != null)
                    foreach (var required in cell.Steps) cell.Reason = cell.Reason.Replace(required.StepId, $"{cell.EntryLabel} · {cell.Camera.Label}", StringComparison.Ordinal);
                Persist();
            }
        }
        await _repository.SaveAsync(run); BoardChanged?.Invoke(this, EventArgs.Empty);
        engine.StepExecutionChanged += OnStep;
        try
        {
            await engine.ExecuteRecipeAsync(prepared.Recipe, token, progress, waitForResumeAsync, run.Id);
            token.ThrowIfCancellationRequested();
            run.Outcome = run.Cells.Any(c => c.State is CapturePlanCellState.Pending or CapturePlanCellState.Running) ? CapturePlanRunOutcome.Failed
                : run.Cells.Any(c => c.State is CapturePlanCellState.Failed or CapturePlanCellState.Skipped) ? CapturePlanRunOutcome.CompletedWithFailures : CapturePlanRunOutcome.Completed;
        }
        catch (OperationCanceledException) { run.Outcome = CapturePlanRunOutcome.Cancelled; throw; }
        catch { run.Outcome = CapturePlanRunOutcome.Failed; throw; }
        finally
        {
            engine.StepExecutionChanged -= OnStep;
            lock (gate)
            {
                foreach (var cell in run.Cells.Where(c => c.State == CapturePlanCellState.Running)) { cell.State = CapturePlanCellState.Failed; cell.Reason = "카메라 절차를 끝까지 완료하지 못했습니다. 재개 시 처음부터 실행합니다."; }
                run.EndedAt = DateTime.UtcNow; Persist();
            }
        }
    }
    private static Dictionary<string, CapturePlanRunCell> Reduce(IEnumerable<CapturePlanRunRecord> chain)
    {
        var receipts = new Dictionary<string, CapturePlanRunCell>(StringComparer.Ordinal);
        foreach (var run in chain) foreach (var cell in run.Cells) if (cell.State != CapturePlanCellState.Excluded) receipts[cell.CellKey] = cell;
        return receipts;
    }
}
