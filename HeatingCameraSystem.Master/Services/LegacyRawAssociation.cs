using System.IO;
using System.Text.RegularExpressions;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services;

public sealed record LegacyRawContext(string SaveRootPath, string? ConditionFolder = null,
    string? RoleFolder = null, string? FilePrefix = null, string Evidence = "레시피 조건 미상");

public sealed record LegacyRawCandidate(string Path, int FolderScore, TimeSpan TimeDifference, string Evidence)
{
    public string Display => $"{Path} · 시각 차이 {TimeDifference.TotalSeconds:F3}초 · {Evidence}";
}

public sealed record LegacyRawDiscovery(IReadOnlyList<LegacyRawCandidate> Candidates, LegacyRawCandidate? Automatic,
    string InitialDirectory, string Note);

/// <summary>Legacy discovery is evidence, not provenance. Only a unique full folder match within ±2 seconds is automatic.</summary>
public static class LegacyRawAssociation
{
    public static readonly TimeSpan AutomaticWindow = TimeSpan.FromSeconds(2);

    public static LegacyRawContext? FromRecipe(CaptureHistoryRecord capture, Recipe recipe)
    {
        if (string.IsNullOrWhiteSpace(recipe.SaveRootPath)) return null;
        string? stepId = RecipeCaptureRequestKey.GetStepId(capture.RecipeStepId, recipe.Steps);
        var step = recipe.Steps.SingleOrDefault(s => s.StepId == stepId);
        if (step is null) return new(recipe.SaveRootPath);
        var targets = step.CameraTargets.Where(t =>
            string.Equals(t.AgentId, capture.AgentId, StringComparison.OrdinalIgnoreCase) &&
            (capture.CameraIndex is null || capture.CameraIndex == t.CameraIndex)).ToArray();
        if (targets.Length != 1 || targets[0].TargetChamber is not ChamberRange range ||
            targets[0].TargetBlackBody is not BlackBodyRole role)
            return new(recipe.SaveRootPath);
        int index = recipe.Steps.IndexOf(step);
        double temperature = recipe.Steps.Take(index).LastOrDefault(s => s.Kind == RecipeStepKind.ChamberControl)
            ?.TargetChamberTemperature ?? capture.Temperature;
        return new(recipe.SaveRootPath, CaptureNamingRule.ConditionFolder(range, temperature),
            CaptureNamingRule.BlackBodyFolder(role), CaptureNamingRule.FilePrefix(range, role),
            $"현재 레시피 기반 추정 · Agent {capture.AgentId}/카메라 {capture.CameraIndex} · 목표/기록 온도 {temperature:G}℃");
    }

    public static LegacyRawDiscovery Discover(CaptureHistoryRecord capture, LegacyRawContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.SaveRootPath))
            return new(Array.Empty<LegacyRawCandidate>(), null, string.Empty, "실행 레시피의 생산 저장 루트를 찾을 수 없습니다.");
        var candidates = new List<LegacyRawCandidate>();
        try
        {
            // Enumerate the production hierarchy only, never unrelated recursive trees or Agent-local paths.
            foreach (string product in Directory.EnumerateDirectories(context.SaveRootPath))
            foreach (string condition in Directory.EnumerateDirectories(product))
            foreach (string role in Directory.EnumerateDirectories(condition))
            {
                string roleName = System.IO.Path.GetFileName(role);
                if (roleName is not ("hot" or "cold" or "room")) continue;
                bool conditionMatch = context.ConditionFolder == System.IO.Path.GetFileName(condition);
                bool roleMatch = context.RoleFolder == roleName;
                foreach (string path in Directory.EnumerateFiles(role, "*.raw"))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!Regex.IsMatch(name, @"^BB(?:room|\d+)_\d{3,}$", RegexOptions.CultureInvariant)) continue;
                    bool prefixMatch = context.FilePrefix is not null && name.StartsWith(context.FilePrefix + "_", StringComparison.Ordinal);
                    int score = (conditionMatch ? 4 : 0) + (roleMatch ? 2 : 0) + (prefixMatch ? 1 : 0);
                    TimeSpan difference = (File.GetLastWriteTimeUtc(path) - capture.Timestamp.ToUniversalTime()).Duration();
                    candidates.Add(new(path, score, difference,
                        $"조건 {(conditionMatch ? "일치" : "미확인/불일치")} · 역할 {(roleMatch ? "일치" : "미확인/불일치")} · BB {(prefixMatch ? "일치" : "미확인/불일치")}"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(candidates.OrderByDescending(c => c.FolderScore).ThenBy(c => c.TimeDifference).ToArray(), null,
                context.SaveRootPath, $"생산 트리 검색 미완료: {ex.Message}");
        }
        var ranked = candidates.OrderByDescending(c => c.FolderScore).ThenBy(c => c.TimeDifference)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var full = ranked.Where(c => c.FolderScore == 7).ToArray();
        LegacyRawCandidate? automatic = null;
        if (full.Select(c => System.IO.Path.GetDirectoryName(c.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 &&
            full.Length > 0 && full[0].TimeDifference <= AutomaticWindow &&
            (full.Length == 1 || full[0].TimeDifference < full[1].TimeDifference))
            automatic = full[0] with { Evidence = $"{context.Evidence} · 조건/역할/BB 일치 폴더 1개 · 파일 수정 시각 차이 {full[0].TimeDifference.TotalSeconds:F3}초 (±2초 이내)" };
        return new(ranked, automatic,
            ranked.Length == 0 ? context.SaveRootPath : System.IO.Path.GetDirectoryName(ranked[0].Path) ?? context.SaveRootPath,
            automatic is null ? "자동 연결 근거가 부족하거나 폴더/촬영 시각이 모호합니다. 후보를 확인하고 원본 .raw를 선택하세요." : automatic.Evidence);
    }

    public static async Task<byte[]> ReadValidatedAsync(string path, int width, int height)
    {
        if (!string.Equals(System.IO.Path.GetExtension(path), ".raw", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("원본 .raw 파일을 선택하세요.");
        if (width <= 0 || height <= 0) throw new InvalidDataException("촬영 해상도를 확인할 수 없습니다. 대표 이미지 또는 Agent 원본을 먼저 가져오세요.");
        long expected = checked((long)width * height * sizeof(ushort));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length != expected) throw new InvalidDataException($"원본 크기 불일치: {width}×{height}×2 = {expected}바이트 필요, 실제 {stream.Length}바이트.");
        var bytes = new byte[checked((int)expected)];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }

    public static async Task SetOperatorLinkAsync(ICaptureHistoryRepository repository, CaptureHistoryRecord capture,
        string path, int width, int height)
    {
        string fullPath = string.IsNullOrWhiteSpace(path) ? string.Empty : System.IO.Path.GetFullPath(path);
        if (fullPath.Length > 0) await ReadValidatedAsync(fullPath, width, height);
        await repository.SetRawLinkAsync(capture.Id, fullPath, width, height);
        capture.LinkedRawPath = fullPath;
        capture.PixelWidth = width;
        capture.PixelHeight = height;
    }
}
