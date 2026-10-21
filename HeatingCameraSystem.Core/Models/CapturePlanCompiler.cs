using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatingCameraSystem.Core.Models;

public sealed class CapturePlanValidationException : ArgumentException
{
    public CapturePlanValidationException(string message) : base(message) { }
}

/// <summary>Live-linked templates are resolved once; their whole sequence repeats per camera without deduplication.</summary>
public static class CapturePlanCompiler
{
    public const string OldFormatMessage = "이전 형식 계획입니다. 다시 만들어 주세요.";
    public static string CellKey(string entryKey, CameraIdentity camera) => $"{entryKey.Length}:{entryKey}/{camera.Key}";
    public static string StepId(string recipeId, string entryKey, CameraIdentity camera, string templateStepId)
    {
        string identity = $"capture-plan-template-v2|{recipeId.Length}:{recipeId}|{entryKey.Length}:{entryKey}|{camera.Key}|{templateStepId.Length}:{templateStepId}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16)).ToString("D");
    }

    public static List<RecipeStep> Compile(Recipe recipe, IReadOnlyList<CameraPositionEntry> positions, IReadOnlyList<Recipe> templates)
        => CompileCore(recipe, positions, templates, null);

    public static List<RecipeStep> CompileRemaining(Recipe recipe, IReadOnlyList<CameraPositionEntry> positions, IReadOnlyList<Recipe> templates, IReadOnlySet<string> excluded)
        => CompileCore(recipe, positions, templates, excluded);

    public static string TemplateFingerprint(CapturePlan plan, IReadOnlyList<Recipe> templates)
    {
        var content = (plan.Entries ?? new()).Select(entry => new
        {
            entry.Key, entry.TemplateRecipeId,
            Steps = templates.FirstOrDefault(r => r.Id == entry.TemplateRecipeId)?.Steps
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content))));
    }

    private static List<RecipeStep> CompileCore(Recipe recipe, IReadOnlyList<CameraPositionEntry> positions, IReadOnlyList<Recipe> templates, IReadOnlySet<string>? excluded)
    {
        CapturePlan plan = recipe.Plan ?? throw new CapturePlanValidationException("촬영 계획이 없습니다.");
        var entries = plan.Entries ?? throw new CapturePlanValidationException(OldFormatMessage);
        Require(!string.IsNullOrWhiteSpace(recipe.Id), "레시피 식별자가 없습니다.");
        Require(entries.Count > 0, "템플릿 레시피를 추가하세요.");
        var lookup = new Dictionary<string, CameraPosition>();
        foreach (var position in positions)
        {
            CameraPositionRules.ValidateEntry(position);
            Require(lookup.TryAdd(PositionKey(position.Camera, position.Role), position.Position), "공유 위치표의 카메라/역할이 중복되었습니다.");
        }
        var result = new List<RecipeStep>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        // Counters are per physical camera and actual production destination, not per plan entry.
        var shotNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var highWater = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownTemperatureGroups = new HashSet<string>(StringComparer.Ordinal);
        double? chamberTarget = null;
        foreach (var entry in entries)
        {
            Require(!string.IsNullOrWhiteSpace(entry.Key) && keys.Add(entry.Key), "계획 항목 키가 비어 있거나 중복되었습니다.");
            Recipe template = templates.FirstOrDefault(r => r.Id == entry.TemplateRecipeId)
                ?? throw new CapturePlanValidationException($"항목 {entries.IndexOf(entry) + 1}: 연결된 템플릿 레시피를 찾을 수 없습니다.");
            string label = $"항목 {entries.IndexOf(entry) + 1} ({template.Name})";
            Require(template.Plan == null, label + ": 촬영 계획을 템플릿으로 사용할 수 없습니다.");
            Require(template.Steps.Count > 0, label + ": 템플릿에 스텝이 없습니다.");
            Require(entry.Cameras.Count > 0, label + ": 연결된 카메라를 선택하세요.");
            Require(!template.Steps.Any(s => s.Kind == RecipeStepKind.CameraCommand) ||
                template.Steps.Any(s => s.Kind == RecipeStepKind.CameraCommand && s.UseCellCamera),
                label + ": 카메라 명령에서 '셀 카메라' 슬롯을 지정하세요.");
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in template.Steps)
            {
                Require(!string.IsNullOrWhiteSpace(source.StepId) && stepIds.Add(source.StepId), label + ": 템플릿 스텝 식별자가 비어 있거나 중복되었습니다.");
                Require(!source.UseCellCamera || source.Kind == RecipeStepKind.CameraCommand, label + ": 셀 카메라 슬롯은 카메라 명령에만 사용할 수 있습니다.");
                Require(source.CellPositionRole == null || source.Kind == RecipeStepKind.MotorMove, label + ": 셀 위치 슬롯은 모터 이동에만 사용할 수 있습니다.");
                Require(source.CameraTargets.Count <= 1 || source.Kind != RecipeStepKind.CameraCommand, label + ": 템플릿은 카메라 한 대용이어야 합니다.");
            }
            var cameras = new HashSet<string>(StringComparer.Ordinal);
            foreach (var camera in entry.Cameras)
            {
                ValidateCamera(camera);
                Require(cameras.Add(camera.Key), label + ": 카메라가 중복 선택되었습니다.");
                string cell = CellKey(entry.Key, camera);
                bool include = excluded?.Contains(cell) != true;
                foreach (var source in template.Steps)
                {
                    var step = source.Copy();
                    step.StepId = StepId(recipe.Id, entry.Key, camera, source.StepId);
                    step.PlanCellKey = cell;
                    if (source.CellPositionRole is { } role)
                    {
                        Require(Enum.IsDefined(role), label + ": 위치 역할이 올바르지 않습니다.");
                        Require(lookup.TryGetValue(PositionKey(camera, role), out var position), $"{label} · {camera.Label} · {role}: 공유 위치가 없습니다.");
                        if (position != null)
                        {
                            step.MotorMoveType = position.Kind == CameraPositionKind.Point ? MotorMoveType.Automatic : MotorMoveType.Manual;
                            step.TargetPositionIndex = position.PointNumber;
                            step.PositionX = position.X; step.PositionY = position.Y;
                        }
                    }
                    else if (source.Kind == RecipeStepKind.MotorMove)
                        CameraPositionRules.Validate(new CameraPosition { Kind = source.MotorMoveType == MotorMoveType.Automatic ? CameraPositionKind.Point : CameraPositionKind.Coordinate, PointNumber = source.TargetPositionIndex, X = source.PositionX, Y = source.PositionY });
                    if (source.UseCellCamera)
                    {
                        var defaults = source.CameraTargets.FirstOrDefault();
                        step.CameraIndex = camera.CameraIndex;
                        step.CameraAlias = null;
                        step.CameraTargets = new() { new() { AgentId = camera.AgentId, CameraIndex = camera.CameraIndex, TargetChamber = defaults?.TargetChamber, TargetBlackBody = defaults?.TargetBlackBody } };
                    }
                    if (include && step.Kind == RecipeStepKind.ChamberControl)
                    {
                        Require(double.IsFinite(step.TargetChamberTemperature), label + ": 챔버 목표 온도는 유한수여야 합니다.");
                        chamberTarget = step.TargetChamberTemperature;
                    }
                    if (step.Kind == RecipeStepKind.CameraCommand && step.CameraOperation == CameraControlOps.Capture)
                    {
                        Require(step.ShotCount > 0, label + ": 촬영 장수는 1 이상이어야 합니다.");
                        var target = step.CameraTargets.FirstOrDefault();
                        if (target?.TargetChamber is { } band && target.TargetBlackBody is { } captureRole)
                        {
                            Require(Enum.IsDefined(band) && Enum.IsDefined(captureRole), label + ": Capture 대상 대역/역할이 올바르지 않습니다.");
                            string agentId = target.AgentId ?? string.Empty;
                            string group = $"{agentId.Length}:{agentId}/{target.CameraIndex}/{band}/{captureRole}";
                            // Capture-only recipes retain the engine's runtime target fallback. If the
                            // initial target is unknown, reserve across temperature codes conservatively.
                            string condition = chamberTarget.HasValue ? CaptureNamingRule.ConditionFolder(band, chamberTarget.Value) : "runtime-target";
                            string folder = $"{group}/{condition}";
                            shotNumbers.TryGetValue(folder, out int offset);
                            highWater.TryGetValue(group, out int maximum);
                            if (!chamberTarget.HasValue || unknownTemperatureGroups.Contains(group)) offset = maximum;
                            // 계획 레시피 자신의 OverwriteDuplicateShots만 본다(템플릿의 값은 무시). 켜져 있으면 모두 000부터 덮어쓴다.
                            step.ShotNumberOffset = recipe.OverwriteDuplicateShots ? 0 : offset;
                            int repeats = step.CaptureIntervalSeconds <= 0 ? 1 : Math.Max(1, step.CaptureDurationSeconds / step.CaptureIntervalSeconds);
                            if (include)
                            {
                                long total = (long)offset + (long)step.ShotCount * repeats;
                                Require(total <= int.MaxValue, label + ": 누적 촬영 번호가 허용 범위를 초과합니다.");
                                int end = (int)total;
                                shotNumbers[folder] = end; highWater[group] = Math.Max(maximum, end);
                                if (!chamberTarget.HasValue) unknownTemperatureGroups.Add(group);
                            }
                        }
                    }
                    if (include) result.Add(step);
                }
            }
        }
        return result;
    }

    public static string PositionKey(CameraIdentity camera, BlackBodyRole role) => $"{camera.Key}/{role}";
    public static void ValidateCamera(CameraIdentity camera)
        => Require(!string.IsNullOrWhiteSpace(camera.AgentId) && camera.CameraIndex >= 0, "실제 AgentId와 카메라 번호(0 이상)가 필요합니다.");
    private static void Require(bool valid, string message) { if (!valid) throw new CapturePlanValidationException(message); }
}
