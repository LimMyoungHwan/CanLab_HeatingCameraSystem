namespace HeatingCameraSystem.Core.Config
{
    public class AgentConfig
    {
        public string AgentId { get; set; } = "";
        public int CameraIndex { get; set; } = 0;
        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";
        public string StoragePath { get; set; } = "ImageStorage";
        public int HeartbeatIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// true이면 실제 카메라(VideoCapture) 대신 합성 이미지(타임스탬프 + 카메라 인덱스 텍스트가 그려진 JPEG)를 만든다.
        /// 카메라/웹캠이 없는 환경에서 E2E 테스트용.
        /// </summary>
        public bool SimulationMode { get; set; } = false;

        public string LogPath { get; set; } = "";

        /// <summary>
        /// [camera-model-select] Design Ref: §1.2 — 카메라 모델명.
        /// 지정 시 CameraModels\{CameraModel}.json 을 읽어 캡처 해상도(Width/Height)를 적용한다.
        /// 미지정(null)이면 모델 스펙 로드를 스킵하고 카메라 기본 해상도를 그대로 사용한다.
        /// </summary>
        public string? CameraModel { get; set; }
    }
}
