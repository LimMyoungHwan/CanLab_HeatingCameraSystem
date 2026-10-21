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
        public double? CameraTemperature { get; set; }

        /// <summary>Agent PC에 저장된 원본 이미지 경로.</summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>레시피 캡처 요청 키(StepId:AgentId:CameraIndex:round), 또는 이전 기록의 StepId. 빈 값이면 비레시피 촬영.</summary>
        public string RecipeStepId { get; set; } = string.Empty;

        /// <summary>
        /// Agent PC에 남아 있는 원본(.y16) 경로. <see cref="ImagePath"/>는 Master가 캐시한 JPEG를
        /// 가리키므로 히스토그램·내보내기에 쓸 원본 위치는 별도로 들고 있어야 한다.
        /// 이 경로는 Master 파일시스템에서 열 수 없으며, 해당 Agent에 요청해서 받아온다.
        /// </summary>
        public string AgentRawPath { get; set; } = string.Empty;

        /// <summary>대표 프레임의 Master 생산 .raw 목적지. 전송 대기·실패로 아직 파일이 없을 수 있다.</summary>
        public string ProductionRawPath { get; set; } = string.Empty;

        /// <summary>운영자가 검증 후 연결한 Master .raw. 자동 기록 경로보다 우선하며 해제 시 비운다.</summary>
        public string LinkedRawPath
        {
            get => _linkedRawPath;
            // LiteDB's default mapper stores empty strings as null.
            set => _linkedRawPath = value ?? string.Empty;
        }
        private string _linkedRawPath = string.Empty;

        /// <summary>원본 프레임 크기. 이전 기록은 미상(0)이며 대표 이미지/Agent 응답에서 확인한다.</summary>
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }

        /// <summary>
        /// 이 캡처를 남긴 레시피 실행 회차. 같은 회차의 측정 기록
        /// (<see cref="RecipeMeasurementRecord.RunId"/>)과 같은 값이며, 결과 화면이 이 키로 묶는다.
        /// 레시피가 아닌 수동/AgentUI 캡처는 비어 있다.
        /// </summary>
        public string RunId { get; set; } = string.Empty;
    }
}
