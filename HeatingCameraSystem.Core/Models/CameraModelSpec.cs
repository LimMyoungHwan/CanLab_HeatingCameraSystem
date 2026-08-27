using System;
using System.IO;
using System.Text.Json;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// [camera-model-select] Design Ref: §1.1 — 카메라 모델별 센서 스펙.
    /// <c>CameraModels\{ModelName}.json</c> 파일과 1:1 대응한다.
    /// 신규 모델 추가 시 이 구조의 JSON 파일만 배치하면 코드 변경 없이 적용된다.
    /// </summary>
    public class CameraModelSpec
    {
        /// <summary>센서 너비(픽셀).</summary>
        public int Width  { get; set; }

        /// <summary>센서 높이(픽셀).</summary>
        public int Height { get; set; }

        /// <summary>
        /// [camera-model-select] Design Ref: §2.2 — <c>modelsDirectory\{modelName}.json</c>을 로드한다.
        /// modelName이 비어 있거나 파일이 없거나 파싱 실패 시 예외 없이 null을 반환한다
        /// (모델 미지정 카메라는 기존 동작을 그대로 유지해야 하므로).
        /// </summary>
        public static CameraModelSpec? Load(string modelsDirectory, string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return null;

            string path = Path.Combine(modelsDirectory, $"{modelName}.json");
            if (!File.Exists(path)) return null;

            try
            {
                return JsonSerializer.Deserialize<CameraModelSpec>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
