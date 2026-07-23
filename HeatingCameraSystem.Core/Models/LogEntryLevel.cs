namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Severity of a parsed log entry, mirroring Serilog's LogEventLevel ordering so a numeric
    /// "minimum level" filter works. Named <c>LogEntryLevel</c> (not <c>LogLevel</c>) to avoid
    /// colliding with <c>Microsoft.Extensions.Logging.LogLevel</c>, which AgentUI pulls in via
    /// Serilog.Extensions.Logging.
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
