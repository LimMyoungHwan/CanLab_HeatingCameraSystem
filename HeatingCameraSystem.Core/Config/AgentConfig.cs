namespace HeatingCameraSystem.Core.Config
{
    /// <summary>
    /// Agent(카메라 PC) 실행 설정. agent.json 으로 직렬화되며, 최초 실행 시 기본값으로 자동 생성된다.
    /// </summary>
    public class AgentConfig
    {
        /// <summary>이 Agent의 고유 식별자. NATS 토픽(agent.status.{AgentId} 등)의 키이며 CameraIndex와 짝이 맞아야 한다.</summary>
        public string AgentId { get; set; } = "";

        /// <summary>이 Agent가 담당하는 카메라 인덱스. 레시피의 RecipeStep.CameraIndex 와 매칭된다.</summary>
        public int CameraIndex { get; set; } = 0;

        /// <summary>접속할 NATS 서버 URL.</summary>
        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";

        /// <summary>캡처 이미지 저장 폴더. 기본은 exe 폴더 하위 ImageStorage.</summary>
        public string StoragePath { get; set; } = "ImageStorage";

        /// <summary>agent.status 하트비트 발행 주기(초).</summary>
        public int HeartbeatIntervalSeconds { get; set; } = 5;

        /// <summary>
        /// true이면 실제 카메라(VideoCapture) 대신 합성 이미지(타임스탬프 + 카메라 인덱스 텍스트가 그려진 JPEG)를 만든다.
        /// 카메라/웹캠이 없는 환경에서 E2E 테스트용.
        /// </summary>
        public bool SimulationMode { get; set; } = false;

        /// <summary>Agent 로그 파일 경로. 비우면 기본 위치를 사용한다.</summary>
        public string LogPath { get; set; } = "";

        /// <summary>
        /// [camera-model-select] Design Ref: §1.2 — 카메라 모델명.
        /// 지정 시 CameraModels\{CameraModel}.json 을 읽어 캡처 해상도(Width/Height)를 적용한다.
        /// 미지정(null)이면 모델 스펙 로드를 스킵하고 카메라 기본 해상도를 그대로 사용한다.
        /// </summary>
        public string? CameraModel { get; set; }
    }
}
