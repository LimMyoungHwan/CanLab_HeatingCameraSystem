using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 촬영 이력 레코드. Master가 Agent로부터 받은 <see cref="CaptureResultMessage"/>를
    /// LiteDB에 저장할 때 쓴다.
    /// </summary>
    public class CaptureHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>카메라 하드웨어 식별자.</summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>Agent NATS 식별자(<c>agent.status.{AgentId}</c>).</summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>운영자가 지정한 카메라 별칭.</summary>
        public string CameraAlias { get; set; } = string.Empty;

        /// <summary>null이면 단일 카메라 Agent이거나 인덱스를 모르는 경우.</summary>
        public int? CameraIndex { get; set; }

        /// <summary>촬영을 유발한 주체. <see cref="CaptureSource"/>.</summary>
        public CaptureSource Source { get; set; } = CaptureSource.Unknown;

        /// <summary>촬영 시점 챔버 온도(℃).</summary>
        public float Temperature { get; set; }

        /// <summary>촬영 시점 챔버 습도(%RH).</summary>
        public float Humidity { get; set; }

        /// <summary>Agent PC에 저장된 원본 이미지 경로.</summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>연관된 레시피 스텝 식별자. 빈 문자열이면 레시피와 무관한 촬영.</summary>
        public string RecipeStepId { get; set; } = string.Empty;
    }
}
