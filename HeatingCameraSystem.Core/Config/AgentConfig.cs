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
    }
}
