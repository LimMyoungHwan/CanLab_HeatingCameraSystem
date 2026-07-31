using System;

namespace HeatingCameraSystem.Core.Models
{
    public class ChamberHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public float Temperature { get; set; }
        public float Humidity { get; set; }
        public float BlackBody1 { get; set; }
        public float BlackBody2 { get; set; }
    }
}
