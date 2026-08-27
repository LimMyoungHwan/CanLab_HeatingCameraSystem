using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 열화상 촬영 하나의 self-describing sidecar 메타데이터. 로컬 인덱스나 DB가 없어도
    /// 아카이브를 해석할 수 있도록 원시 <c>.y16</c> 파일 옆에 저장된다.
    /// 원시 페이로드는 little endian <see cref="ushort"/>이며 14bit로 마스킹된다.
    /// </summary>
    public sealed class CaptureMetadata
    {
        /// <summary>Agent NATS 식별자.</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>Agent 내 카메라 인덱스.</summary>
        public int CameraIndex { get; set; }

        /// <summary>이미지 너비(픽셀).</summary>
        public int Width { get; set; }

        /// <summary>이미지 높이(픽셀).</summary>
        public int Height { get; set; }

        /// <summary>픽셀 형식. 기본값은 <c>Y16_14bit_LE</c>.</summary>
        public string PixelFormat { get; set; } = "Y16_14bit_LE";

        /// <summary>UTC 촬영 시각.</summary>
        public DateTimeOffset TimestampUtc { get; set; }

        /// <summary>원시 픽셀 최소값(14bit 범위).</summary>
        public ushort Min { get; set; }

        /// <summary>원시 픽셀 최대값(14bit 범위).</summary>
        public ushort Max { get; set; }

        /// <summary>연관 레시피 스텝 식별자. null이면 레시피 외 촬영.</summary>
        public string? RecipeStepId { get; set; }

        /// <summary>원시 Y16 파일 이름.</summary>
        public string Y16File { get; set; } = string.Empty;
    }
}
