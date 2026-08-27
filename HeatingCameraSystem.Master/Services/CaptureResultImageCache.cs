using System;
using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// Agent가 캡처 결과에 실어 보낸 JPEG 미리보기 바이트를 Master 로컬 ImageCache 폴더에
    /// 파일로 저장하는 헬퍼. 이력 화면이 Agent PC에 접근하지 않고도 이미지를 볼 수 있게 한다.
    /// </summary>
    public static class CaptureResultImageCache
    {
        /// <summary>
        /// 미리보기 바이트를 캐시 폴더에 .jpg로 저장하고 전체 경로를 돌려준다.
        /// 바이트가 없거나 캐시 폴더 미지정, 쓰기 실패면 null이다.
        /// </summary>
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
