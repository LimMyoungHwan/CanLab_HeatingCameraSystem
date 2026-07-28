using System;
using System.Collections.Generic;

namespace HeatingCameraSystem.Core.Models
{
    public class AgentConfigSnapshot
    {
        public bool SimulationMode { get; set; }
        public string NatsUrl { get; set; } = "nats://127.0.0.1:4222";
        public string StoragePath { get; set; } = string.Empty;
        public int HeartbeatSeconds { get; set; } = 5;
        public CaptureImageFormat CaptureImageFormat { get; set; } = CaptureImageFormat.Y16Raw;
        public int CaptureBurstCount { get; set; } = 1;
        public List<CameraDescriptor> Cameras { get; set; } = new();
    }

    public class AgentConfigRequestMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class AgentConfigSnapshotMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public AgentConfigSnapshot Config { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class AgentConfigApplyMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public AgentConfigSnapshot Config { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class AgentConfigAckMessage
    {
        public string AgentId { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
