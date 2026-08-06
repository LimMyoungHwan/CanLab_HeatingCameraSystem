using System;
using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    public static class CaptureResultImageCache
    {
        // ImageBytes는 항상 JPEG 프리뷰(ThermalPreviewEncoder)다. 확장자를 Agent의 .y16 ImagePath에서
        // 따오면 JPEG를 .y16 파일에 써 넣게 되므로 .jpg로 고정한다.
        public static string? Store(CaptureResultMessage result, string? imageCacheDir)
        {
            if (result.ImageBytes == null || result.ImageBytes.Length == 0) return null;
            if (string.IsNullOrEmpty(imageCacheDir)) return null;

            try
            {
                Directory.CreateDirectory(imageCacheDir);
                string stepPart = string.IsNullOrEmpty(result.RecipeStepId) ? "manual" : result.RecipeStepId;
                string filename = $"{result.AgentId}_{result.Timestamp:yyyyMMdd_HHmmss_fff}_{stepPart}.jpg";
                foreach (char c in Path.GetInvalidFileNameChars())
                    filename = filename.Replace(c, '_');
                string fullPath = Path.Combine(imageCacheDir, filename);
                File.WriteAllBytes(fullPath, result.ImageBytes);
                return fullPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureImageCache] write failed: {ex.Message}");
                return null;
            }
        }
    }
}
