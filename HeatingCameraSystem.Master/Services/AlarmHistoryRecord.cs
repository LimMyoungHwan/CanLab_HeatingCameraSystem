using System;

namespace HeatingCameraSystem.Master.Services
{
    public sealed class AlarmHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public AlarmSeverity Severity { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
