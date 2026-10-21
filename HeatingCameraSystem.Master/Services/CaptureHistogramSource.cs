using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services;

public sealed record CaptureHistogramResult(HistogramAnalysis Analysis, string Source, string Note);

/// <summary>Every load probes production again; a cached .y16 never hides an available .raw.</summary>
public static class CaptureHistogramSource
{
    public static async Task<CaptureHistogramResult?> LoadAsync(
        CaptureHistoryRecord capture, Func<Task<byte[]?>> loadRepresentative, LegacyRawDiscovery? discovery = null)
    {
        string path = !string.IsNullOrWhiteSpace(capture.LinkedRawPath) ? capture.LinkedRawPath : capture.ProductionRawPath;
        string origin = !string.IsNullOrWhiteSpace(capture.LinkedRawPath) ? "운영자 지정" : "자동 기록";
        if (string.IsNullOrWhiteSpace(path) && discovery?.Automatic is LegacyRawCandidate candidate)
        {
            path = candidate.Path;
            origin = $"자동 추정: {candidate.Evidence}";
        }
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                // Missing files alone permit fallback. A corrupt/unreadable existing RAW must remain an error.
                byte[] pixels = capture.PixelWidth > 0 && capture.PixelHeight > 0
                    ? await LegacyRawAssociation.ReadValidatedAsync(path, capture.PixelWidth, capture.PixelHeight)
                    : await File.ReadAllBytesAsync(path);
                return new CaptureHistogramResult(RawHistogram.Analyze(pixels, excludeFirstPixel: true),
                    $"원본 .raw ({origin}): {path}", "대표 촬영 1장 · NUC 미보정 · 픽셀 (0,0) 제외");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        byte[]? representative = await loadRepresentative();
        if (representative is null) return null;
        string reason = string.IsNullOrWhiteSpace(path)
            ? "원본 위치 정보 없음(이전 기록)"
            : "원본 .raw 없음/미전송";
        string note = string.IsNullOrWhiteSpace(path)
            ? $"{reason}. 파일 부재를 확인한 것은 아닙니다. {discovery?.Note} 원본 .raw 선택으로 연결할 수 있습니다."
            : $"확인한 경로에 원본 .raw가 없습니다 (Agent 버퍼/전송 대기 가능). 다음 조회 때 재확인합니다. 확인 경로: {path}";
        return new CaptureHistogramResult(RawHistogram.Analyze(representative),
            $".y16 (NUC 보정) — {reason}", note);
    }
}
