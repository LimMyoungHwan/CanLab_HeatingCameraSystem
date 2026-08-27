namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// 파싱된 로그 항목의 심각도. Serilog LogEventLevel 순서를 그대로 따라야
    /// "최소 레벨" 필터가 올바르게 동작한다.
    /// <c>LogLevel</c>이 아닌 <c>LogEntryLevel</c>로 이름을 정한 이유는
    /// AgentUI가 Serilog.Extensions.Logging을 사용하면서 끌어오는
    /// <c>Microsoft.Extensions.Logging.LogLevel</c>과 충돌하지 않기 위해서다.
    /// </summary>
    public enum LogEntryLevel
    {
        Verbose,
        Debug,
        Information,
        Warning,
        Error,
        Fatal
    }
}
