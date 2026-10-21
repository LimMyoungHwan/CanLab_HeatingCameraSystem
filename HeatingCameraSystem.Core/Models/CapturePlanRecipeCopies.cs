namespace HeatingCameraSystem.Core.Models;

public static class CapturePlanRecipeCopies
{
    public static Recipe Clone(Recipe source) => new()
    {
        Id = source.Id, Name = source.Name, TemperatureRampMinutes = source.TemperatureRampMinutes,
        RecordOnTemperatureDelta = source.RecordOnTemperatureDelta, RecordOnHumidityDelta = source.RecordOnHumidityDelta,
        RecordIntervalSeconds = source.RecordIntervalSeconds, SaveRootPath = source.SaveRootPath,
        ProductNumber = source.ProductNumber, SaveFormat = source.SaveFormat, KeepChamberRunning = source.KeepChamberRunning,
        OverwriteDuplicateShots = source.OverwriteDuplicateShots,
        Plan = source.Plan == null ? null : ClonePlan(source.Plan), PlanValidationError = source.PlanValidationError,
        Steps = source.Steps.Select(step => step.Copy()).ToList()
    };
    public static CapturePlan ClonePlan(CapturePlan source) => new()
    {
        Entries = source.Entries?.Select(entry => new CapturePlanEntry
        {
            Key = entry.Key, TemplateRecipeId = entry.TemplateRecipeId, Cameras = entry.Cameras.Select(c => c.Copy()).ToList()
        }).ToList()
    };
    public static CameraPosition? ClonePosition(CameraPosition? source) => source == null ? null : new()
    {
        Kind = source.Kind, X = source.X, Y = source.Y, PointNumber = source.PointNumber
    };
    public static Recipe ToLegacyCopy(Recipe source, IReadOnlyList<CameraPositionEntry> positions, IReadOnlyList<Recipe> templates)
    {
        Recipe copy = Clone(source);
        copy.Steps = source.Plan == null ? source.Steps.Select(step => step.Copy()).ToList() : CapturePlanCompiler.Compile(source, positions, templates);
        copy.Id = Guid.NewGuid().ToString(); copy.Name = source.Name + " (스텝 목록)";
        copy.Plan = null; copy.PlanValidationError = null;
        foreach (RecipeStep step in copy.Steps)
        {
            step.StepId = Guid.NewGuid().ToString(); step.PlanCellKey = null;
            step.UseCellCamera = false; step.CellPositionRole = null;
        }
        return copy;
    }
}
