using System;

namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// One parsed log line for the AgentUI log viewer. Sourced from a Serilog compact-JSON
    /// (CLEF) <c>.ndjson</c> file: <c>@t</c> → <see cref="TimestampUtc"/>, <c>@l</c> →
    /// <see cref="Level"/> (absent means Information), <c>@m</c>/<c>@mt</c> → <see cref="Message"/>,
    /// <c>@x</c> → <see cref="Exception"/>.
    /// </summary>
    public sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        LogEntryLevel Level,
        string Message,
        string? Exception);
}
