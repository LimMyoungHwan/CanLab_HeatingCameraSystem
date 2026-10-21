using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CapturePlanCompilerTests
{
    [Fact]
    public void RealMp10_RepeatsExactSequenceForTwoActualCameras()
    {
        var template = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(template);
        string before = JsonSerializer.Serialize(template);
        var steps = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { template });
        Assert.Equal(20, steps.Count);
        Assert.Equal(template.Steps.Select(s => s.Kind).Concat(template.Steps.Select(s => s.Kind)), steps.Select(s => s.Kind));
        Assert.Equal(2, steps.Count(s => s.Kind == RecipeStepKind.ChamberControl));
        Assert.Equal(2, steps.Count(s => s.Kind == RecipeStepKind.BlackBodyControl));
        Assert.Equal(new[] { 1800, 1800 }, steps.Where(s => s.Kind == RecipeStepKind.Wait).Select(s => s.WaitDurationSeconds));
        Assert.Equal(new[] { 12, 13, 14, 22, 23, 24 }, steps.Where(s => s.Kind == RecipeStepKind.MotorMove).Select(s => s.TargetPositionIndex));
        foreach (var group in steps.Chunk(10).Select((list, index) => (list, index)))
        {
            var camera = TemplatePlanTestData.Cameras[group.index];
            Assert.All(group.list.Where(s => s.Kind == RecipeStepKind.CameraCommand), s =>
            {
                var target = Assert.Single(s.CameraTargets); Assert.Equal(camera.AgentId, target.AgentId); Assert.Equal(camera.CameraIndex, target.CameraIndex); Assert.Equal(ChamberRange.Mid, target.TargetChamber);
            });
            Assert.Equal(new BlackBodyRole?[] { BlackBodyRole.Cold, BlackBodyRole.Hot, BlackBodyRole.Room }, group.list.Where(s => s.CameraOperation == CameraControlOps.Capture && s.Kind == RecipeStepKind.CameraCommand).Select(s => s.CameraTargets[0].TargetBlackBody));
        }
        Assert.All(steps.Where(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture), s => Assert.Equal(0, s.ShotNumberOffset));
        Assert.Equal(before, JsonSerializer.Serialize(template));
    }
    [Fact]
    public void OffsetsContinueAcrossCapturesRoundsEntriesAndSnappedTemperatureCodes()
    {
        var source = TemplatePlanTestData.Template();
        var first = source.Steps[5]; first.CaptureIntervalSeconds = 2; first.CaptureDurationSeconds = 6;
        var second = first.Copy(); second.StepId = "second-cold"; second.ShotCount = 7; second.CaptureIntervalSeconds = 0;
        source.Steps.Insert(6, second);
        var plan = TemplatePlanTestData.Plan(source); TemplatePlanTestData.Entries(plan).Add(new() { Key = "again", TemplateRecipeId = source.Id, Cameras = new() { TemplatePlanTestData.Cameras[0].Copy() } });
        var cold = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source }).Where(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture && s.CameraTargets[0].TargetBlackBody == BlackBodyRole.Cold).ToArray();
        Assert.Equal(new[] { 0, 300, 0, 300, 307, 607 }, cold.Select(s => s.ShotNumberOffset));
    }
    [Fact]
    public void OverwriteDuplicateShots_OnPlan_AssignsZeroOffsetToEveryCapture_TemplateFlagIgnored()
    {
        var source = TemplatePlanTestData.Template();
        var first = source.Steps[5]; first.CaptureIntervalSeconds = 2; first.CaptureDurationSeconds = 6;
        var second = first.Copy(); second.StepId = "second-cold"; second.ShotCount = 7; second.CaptureIntervalSeconds = 0;
        source.Steps.Insert(6, second);
        var plan = TemplatePlanTestData.Plan(source); TemplatePlanTestData.Entries(plan).Add(new() { Key = "again", TemplateRecipeId = source.Id, Cameras = new() { TemplatePlanTestData.Cameras[0].Copy() } });

        source.OverwriteDuplicateShots = true; plan.OverwriteDuplicateShots = false;
        var kept = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source }).Where(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture);
        Assert.Contains(kept, s => s.ShotNumberOffset > 0);

        source.OverwriteDuplicateShots = false; plan.OverwriteDuplicateShots = true;
        var captures = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source }).Where(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture).ToArray();
        Assert.NotEmpty(captures);
        Assert.All(captures, s => Assert.Equal(0, s.ShotNumberOffset));
    }
    [Fact]
    public void DeterministicIdsSurviveCameraOrderingAndOtherCameraRemoval()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var first = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source });
        var cameras = TemplatePlanTestData.Entries(plan)[0].Cameras; cameras.Reverse(); cameras.RemoveAt(0);
        var next = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source });
        Assert.Equal(first.Take(10).Select(s => s.StepId), next.Select(s => s.StepId));
        Assert.Equal(20, first.Select(s => s.StepId).Distinct().Count());
    }
    [Fact]
    public void LiveLinkEditChangesNextCompilationButNotExistingExpansion()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var first = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source });
        source.Steps[5].ShotCount = 17;
        var second = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source });
        Assert.Equal(100, first[5].ShotCount); Assert.Equal(17, second[5].ShotCount); Assert.Equal(first[5].StepId, second[5].StepId);
    }
    [Fact]
    public void ResumeExcludesWholeCameraButRepeatsAllRemainingPreconditions()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var excluded = new HashSet<string> { CapturePlanCompiler.CellKey("entry", TemplatePlanTestData.Cameras[0]) };
        var steps = CapturePlanCompiler.CompileRemaining(plan, TemplatePlanTestData.Positions(), new[] { source }, excluded);
        Assert.Equal(10, steps.Count); Assert.Equal(RecipeStepKind.ChamberControl, steps[0].Kind); Assert.Equal(RecipeStepKind.BlackBodyControl, steps[1].Kind); Assert.Equal(1800, steps[3].WaitDurationSeconds);
        Assert.Equal(TemplatePlanTestData.Cameras[1].AgentId, steps[5].CameraTargets[0].AgentId);
    }
    [Fact]
    public void MissingRoomPositionIsDraftErrorRatherThanUsingDefaultPoint()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var error = Assert.Throws<CapturePlanValidationException>(() => CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions().Where(p => p.Role != BlackBodyRole.Room).ToArray(), new[] { source }));
        Assert.Contains("Room", error.Message); Assert.Contains("공유 위치", error.Message);
    }
    [Theory]
    [InlineData("missing-template")][InlineData("nested")][InlineData("duplicate-step")][InlineData("point1")][InlineData("duplicate-camera")]
    public void InvalidPlansFailBeforeExecution(string kind)
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source); var positions = TemplatePlanTestData.Positions();
        if (kind == "missing-template") TemplatePlanTestData.Entries(plan)[0].TemplateRecipeId = "gone";
        if (kind == "nested") source.Plan = new CapturePlan { Entries = new() };
        if (kind == "duplicate-step") source.Steps[1].StepId = source.Steps[0].StepId;
        if (kind == "point1") positions[0].Position.PointNumber = 1;
        if (kind == "duplicate-camera") TemplatePlanTestData.Entries(plan)[0].Cameras.Add(TemplatePlanTestData.Cameras[0].Copy());
        Assert.ThrowsAny<ArgumentException>(() => CapturePlanCompiler.Compile(plan, positions, new[] { source }));
    }
    [Fact]
    public void SnappedTemperatureFoldersShareCountersButDifferentCodesDoNot()
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var near = CapturePlanRecipeCopies.Clone(source); near.Id = "near"; near.Steps[0].TargetChamberTemperature = 12;
        var other = CapturePlanRecipeCopies.Clone(source); other.Id = "other"; other.Steps[0].TargetChamberTemperature = 40;
        TemplatePlanTestData.Entries(plan).Add(new() { Key = "near", TemplateRecipeId = near.Id, Cameras = new() { TemplatePlanTestData.Cameras[0].Copy() } });
        TemplatePlanTestData.Entries(plan).Add(new() { Key = "other", TemplateRecipeId = other.Id, Cameras = new() { TemplatePlanTestData.Cameras[0].Copy() } });
        var cold = CapturePlanCompiler.Compile(plan, TemplatePlanTestData.Positions(), new[] { source, near, other })
            .Where(s => s.Kind == RecipeStepKind.CameraCommand && s.CameraOperation == CameraControlOps.Capture && s.CameraTargets[0].TargetBlackBody == BlackBodyRole.Cold);
        Assert.Equal(new[] { 0, 0, 100, 0 }, cold.Select(s => s.ShotNumberOffset));
    }
    [Fact]
    public void CaptureOnlyTemplateRetainsSequenceAndRuntimeTemperatureFallback()
    {
        var source = TemplatePlanTestData.Template(); source.Steps = new() { source.Steps[5] };
        var plan = TemplatePlanTestData.Plan(source);
        var steps = CapturePlanCompiler.Compile(plan, Array.Empty<CameraPositionEntry>(), new[] { source });
        Assert.Equal(2, steps.Count); Assert.All(steps, s => Assert.Equal(RecipeStepKind.CameraCommand, s.Kind));
        Assert.Equal(TemplatePlanTestData.Cameras.Select(c => c.AgentId), steps.Select(s => s.CameraTargets[0].AgentId));
    }
    [Fact]
    public void PreparationOnlyRecipeRepeatsWholeSequenceWithoutInventedCaptures()
    {
        var source = TemplatePlanTestData.Template(); source.Steps = source.Steps.Take(3).ToList();
        var steps = CapturePlanCompiler.Compile(TemplatePlanTestData.Plan(source), TemplatePlanTestData.Positions(), new[] { source });
        Assert.Equal(6, steps.Count);
        Assert.Equal(new[] { RecipeStepKind.ChamberControl, RecipeStepKind.BlackBodyControl, RecipeStepKind.MotorMove, RecipeStepKind.ChamberControl, RecipeStepKind.BlackBodyControl, RecipeStepKind.MotorMove }, steps.Select(s => s.Kind));
        Assert.Equal(new[] { 12, 22 }, steps.Where(s => s.Kind == RecipeStepKind.MotorMove).Select(s => s.TargetPositionIndex));
    }
    [Fact]
    public void OldFormatIsExplicitlyRejectedEvenWithCachedSteps()
    {
        var old = Assert.IsType<Recipe>(JsonSerializer.Deserialize<Recipe>("{\"Name\":\"old\",\"Plan\":{\"Conditions\":[],\"Pattern\":{}},\"Steps\":[{\"Kind\":2}]}"));
        var error = Assert.Throws<CapturePlanValidationException>(() => CapturePlanCompiler.Compile(old, Array.Empty<CameraPositionEntry>(), Array.Empty<Recipe>()));
        Assert.Equal(CapturePlanCompiler.OldFormatMessage, error.Message);
    }
}

internal static class TemplatePlanTestData
{
    internal static readonly CameraIdentity[] Cameras = { new() { AgentId = "DESKTOP-3T03TKP_Agent_1", CameraIndex = 0 }, new() { AgentId = "DESKTOP-3T03TKP_Agent_5", CameraIndex = 0 } };
    internal static Recipe Template() => new()
    {
        Id = "mp10-template", Name = "MP10", Steps = new()
        {
            new() { StepId = "temperature", Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 10 },
            new() { StepId = "blackbodies", Kind = RecipeStepKind.BlackBodyControl, TargetBlackBodyTemperature = 80, TargetBlackBodyTemperature1 = 20 },
            Move("cold-position", BlackBodyRole.Cold, 12), new() { StepId = "soak", Kind = RecipeStepKind.Wait, WaitDurationSeconds = 1800 },
            Command("bias", CameraControlOps.BiasMid, BlackBodyRole.Cold), Command("cold", CameraControlOps.Capture, BlackBodyRole.Cold),
            Move("hot-position", BlackBodyRole.Hot, 13), Command("hot", CameraControlOps.Capture, BlackBodyRole.Hot),
            Move("room-position", BlackBodyRole.Room, 14), Command("room", CameraControlOps.Capture, BlackBodyRole.Room)
        }
    };
    private static RecipeStep Move(string id, BlackBodyRole role, int point) => new() { StepId = id, Kind = RecipeStepKind.MotorMove, CellPositionRole = role, MotorMoveType = MotorMoveType.Automatic, TargetPositionIndex = point };
    private static RecipeStep Command(string id, string op, BlackBodyRole role) => new() { StepId = id, Kind = RecipeStepKind.CameraCommand, UseCellCamera = true, CameraOperation = op, ShotCount = op == CameraControlOps.Capture ? 100 : 1, BiasTargetLevel = op == CameraControlOps.Capture ? 0 : 8500, CameraTargets = new() { new() { AgentId = Cameras[0].AgentId, CameraIndex = 0, TargetChamber = ChamberRange.Mid, TargetBlackBody = role } } };
    internal static Recipe Plan(Recipe source) => new() { Id = "plan", Name = "연결 촬영 계획", Plan = new CapturePlan { Entries = new() { new() { Key = "entry", TemplateRecipeId = source.Id, Cameras = Cameras.Select(c => c.Copy()).ToList() } } } };
    internal static List<CapturePlanEntry> Entries(Recipe recipe) => Assert.IsType<List<CapturePlanEntry>>(Assert.IsType<CapturePlan>(recipe.Plan).Entries);
    internal static List<CameraPositionEntry> Positions() => Cameras.SelectMany((camera, i) => new[] { BlackBodyRole.Cold, BlackBodyRole.Hot, BlackBodyRole.Room }.Select((role, j) => new CameraPositionEntry { AgentId = camera.AgentId, CameraIndex = camera.CameraIndex, Role = role, Position = new CameraPosition { Kind = CameraPositionKind.Point, PointNumber = 12 + i * 10 + j } })).ToList();
}
