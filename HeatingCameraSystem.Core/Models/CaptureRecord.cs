using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Agent에서 저장한 원시 촬영 기록. 캡처 시점의 메타데이터와 파일 경로를 담는다.
    /// </summary>
    public sealed class CaptureRecord
    {
        public Guid Id { get; set; }

        /// <summary>Agent NATS 식별자.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>Agent 내 카메라 인덱스.</summary>
        public int CameraIndex { get; set; }

        /// <summary>UTC 촬영 시각.</summary>
        public DateTime TimestampUtc { get; set; }

        /// <summary>이미지 너비(픽셀).</summary>
        public int Width { get; set; }

        /// <summary>이미지 높이(픽셀).</summary>
        public int Height { get; set; }

        /// <summary>원시 픽셀 최소값(14bit 해상도 범위 내).</summary>
        public ushort Min { get; set; }

        /// <summary>원시 픽셀 최대값(14bit 해상도 범위 내).</summary>
        public ushort Max { get; set; }

        /// <summary>연관 레시피 스텝 식별자. null이면 레시피 외 촬영.</summary>
        public string? RecipeStepId { get; set; }

        /// <summary>Y16 원시 파일 경로.</summary>
        public string Y16Path { get; set; } = string.Empty;

        /// <summary>JSON 메타데이터 파일 경로.</summary>
        public string JsonPath { get; set; } = string.Empty;

        /// <summary>PNG 미리보기 파일 경로. null이면 미리보기를 만들지 않은 경우.</summary>
        public string? PngPath { get; set; }
    }
}
