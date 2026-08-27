using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// AgentUI 로그 뷰어용 파싱된 로그 한 줄. Serilog CLEF(compact-JSON) <c>.ndjson</c> 파일에서 파생한다.
    /// <c>@t</c> → <see cref="TimestampUtc"/>, <c>@l</c> → <see cref="Level"/>(없으면 Information),
    /// <c>@m</c>/<c>@mt</c> → <see cref="Message"/>, <c>@x</c> → <see cref="Exception"/>.
    /// </summary>
    public sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        LogEntryLevel Level,
        string Message,
        string? Exception);
}
