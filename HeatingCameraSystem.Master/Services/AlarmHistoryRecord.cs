using System;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// LiteDB에 영속되는 알람 이력 1건. <see cref="AlarmSink.Raise"/>가 실시간 목록에 넣으면서
    /// 함께 저장하며, 알람 이력 화면이 이 레코드를 조회한다.
    /// </summary>
    public sealed class AlarmHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public AlarmSeverity Severity { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
