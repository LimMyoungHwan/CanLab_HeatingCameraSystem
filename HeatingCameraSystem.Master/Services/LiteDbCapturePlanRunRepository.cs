using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using LiteDB;

namespace HeatingCameraSystem.Master.Services;

/// <summary>Live run receipts; capture history is deliberately not a completion source.</summary>
public sealed class LiteDbCapturePlanRunRepository : ICapturePlanRunRepository
{
    private readonly ILiteCollection<CapturePlanRunRecord> _runs;

    public LiteDbCapturePlanRunRepository(LiteDatabase database)
    {
        _runs = database.GetCollection<CapturePlanRunRecord>("capture_plan_runs");
        _runs.EnsureIndex(run => run.RecipeId);
        _runs.EnsureIndex(run => run.RootRunId);
        _runs.EnsureIndex(run => run.StartedAt);
    }

    public Task SaveAsync(CapturePlanRunRecord run)
    {
        _runs.Upsert(Copy(run));
        return Task.CompletedTask;
    }

    public Task<CapturePlanRunRecord?> GetByIdAsync(string runId)
    {
        var run = _runs.FindById(runId);
        return Task.FromResult(run == null ? null : Copy(run));
    }

    public Task<IReadOnlyList<CapturePlanRunRecord>> GetLatestChainAsync(string recipeId)
    {
        var runs = _runs.Find(run => run.RecipeId == recipeId && run.SchemaVersion == 2).ToList();
        var root = runs.Where(run => run.Id == run.RootRunId)
            .OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id, StringComparer.Ordinal).FirstOrDefault();
        IReadOnlyList<CapturePlanRunRecord> chain = root == null ? Array.Empty<CapturePlanRunRecord>()
            : runs.Where(run => run.RootRunId == root.Id).OrderBy(run => run.StartedAt)
                .ThenBy(run => run.Id, StringComparer.Ordinal).Select(Copy).ToList();
        return Task.FromResult(chain);
    }

    public Task RecoverInterruptedAsync()
    {
        foreach (var run in _runs.Find(run => run.Outcome == CapturePlanRunOutcome.Running && run.SchemaVersion == 2).ToList())
        {
            run.Outcome = CapturePlanRunOutcome.Interrupted;
            run.EndedAt = DateTime.UtcNow;
            foreach (var cell in run.Cells.Where(cell => cell.State == CapturePlanCellState.Running))
            {
                cell.State = CapturePlanCellState.Failed;
                cell.Reason = "프로그램이 종료되어 촬영 완료를 확인하지 못했습니다.";
            }
            _runs.Upsert(run);
        }
        return Task.CompletedTask;
    }

    private static CapturePlanRunRecord Copy(CapturePlanRunRecord run) => new()
    {
        SchemaVersion = run.SchemaVersion, Id = run.Id, RootRunId = run.RootRunId, ParentRunId = run.ParentRunId,
        RecipeId = run.RecipeId, RecipeName = run.RecipeName, TemplateFingerprint = run.TemplateFingerprint,
        StartedAt = run.StartedAt, EndedAt = run.EndedAt, Outcome = run.Outcome,
        Cells = run.Cells.Select(cell => new CapturePlanRunCell
        {
            CellKey = cell.CellKey, EntryKey = cell.EntryKey, EntryLabel = cell.EntryLabel, Camera = cell.Camera.Copy(),
            State = cell.State, Reason = cell.Reason,
            Steps = cell.Steps.Select(step => new CapturePlanStepReceipt
            {
                StepId = step.StepId, Kind = step.Kind, Operation = step.Operation, Role = step.Role,
                Outcome = step.Outcome, Reason = step.Reason
            }).ToList()
        }).ToList()
    };
}
