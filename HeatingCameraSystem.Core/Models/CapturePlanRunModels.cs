namespace HeatingCameraSystem.Core.Models;

public enum CapturePlanCellState { Pending, Running, Done, Failed, Skipped, Excluded }
public enum CapturePlanRunMode { New, Remaining, FailedOnly }
public enum CapturePlanRunOutcome { Running, Completed, CompletedWithFailures, Cancelled, Failed, Interrupted }
public enum RecipeStepOutcome { Started, Succeeded, Failed, Skipped }

public sealed class CapturePlanStepReceipt
{
    public string StepId { get; set; } = string.Empty;
    public RecipeStepKind Kind { get; set; }
    public string Operation { get; set; } = string.Empty;
    public BlackBodyRole? Role { get; set; }
    public RecipeStepOutcome? Outcome { get; set; }
    public string? Reason { get; set; }
    public string Label => $"{(Role.HasValue ? Role + " · " : string.Empty)}{(Kind == RecipeStepKind.CameraCommand ? CapturePlanLabels.Operation(Operation) : Kind.ToString())}: " + (Outcome switch { RecipeStepOutcome.Succeeded => "완료", RecipeStepOutcome.Failed => "실패", RecipeStepOutcome.Skipped => "건너뜀", RecipeStepOutcome.Started => "진행", _ => "대기" });
}

public sealed class CapturePlanRunCell
{
    public string CellKey { get; set; } = string.Empty;
    public string EntryKey { get; set; } = string.Empty;
    public string EntryLabel { get; set; } = string.Empty;
    public CameraIdentity Camera { get; set; } = new();
    public List<CapturePlanStepReceipt> Steps { get; set; } = new();
    public CapturePlanCellState State { get; set; }
    public string? Reason { get; set; }
}

public sealed class CapturePlanRunRecord
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RootRunId { get; set; } = string.Empty;
    public string? ParentRunId { get; set; }
    public string RecipeId { get; set; } = string.Empty;
    public string RecipeName { get; set; } = string.Empty;
    public string TemplateFingerprint { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public CapturePlanRunOutcome Outcome { get; set; }
    public List<CapturePlanRunCell> Cells { get; set; } = new();
}

public sealed class RecipeStepExecution : EventArgs
{
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public string? PlanCellKey { get; init; }
    public int StepIndex { get; init; }
    public RecipeStepKind Kind { get; init; }
    public int CameraIndex { get; init; }
    public RecipeStepOutcome Outcome { get; init; }
    public string? Reason { get; init; }
}

public sealed class CapturePlanBoard
{
    public string? RootRunId { get; init; }
    public string Warning { get; set; } = string.Empty;
    public List<CapturePlanBoardEntry> Entries { get; init; } = new();
    public IEnumerable<CapturePlanBoardCell> Cells => Entries.SelectMany(e => e.Cells);
    public int TotalCells => Cells.Count(c => c.State != CapturePlanCellState.Excluded);
    public int DoneCells => Cells.Count(c => c.State == CapturePlanCellState.Done);
    public int FailedCells => Cells.Count(c => c.State == CapturePlanCellState.Failed);
    public int SkippedCells => Cells.Count(c => c.State == CapturePlanCellState.Skipped);
    public string Summary => $"완료 {DoneCells} / {TotalCells}" + (FailedCells > 0 ? $" · 실패 {FailedCells}" : "") + (SkippedCells > 0 ? $" · 건너뜀 {SkippedCells}" : "");
}
public sealed class CapturePlanBoardEntry
{
    public string Label { get; init; } = string.Empty;
    public List<CapturePlanBoardCell> Cells { get; init; } = new();
}
public sealed class CapturePlanBoardCell
{
    public string CellKey { get; init; } = string.Empty;
    public CameraIdentity Camera { get; init; } = new();
    public bool IsOnline { get; init; }
    public string CameraLabel => Camera.Label + (IsOnline ? " · 온라인" : " · 오프라인");
    public CapturePlanCellState State { get; init; }
    public string? Reason { get; init; }
    public List<CapturePlanStepReceipt> Steps { get; init; } = new();
    public IEnumerable<CapturePlanStepReceipt> Captures => Steps.Where(s => s.Kind == RecipeStepKind.CameraCommand);
    public bool IsCurrent => State == CapturePlanCellState.Running;
    public string Label => State switch { CapturePlanCellState.Pending => "대기", CapturePlanCellState.Running => "진행", CapturePlanCellState.Done => "완료", CapturePlanCellState.Failed => "실패", CapturePlanCellState.Skipped => "건너뜀", _ => "제외" };
}
