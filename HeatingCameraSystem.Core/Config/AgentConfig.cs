namespace HeatingCameraSystem.Core.Config
{
    public class AgentConfig
    {
        public string AgentId { get; set; } = "";
        public int CameraIndex { get; set; } = 0;
        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";
        public string StoragePath { get; set; } = "ImageStorage";
        public int HeartbeatIntervalSeconds { get; set; } = 5;
    }
}
