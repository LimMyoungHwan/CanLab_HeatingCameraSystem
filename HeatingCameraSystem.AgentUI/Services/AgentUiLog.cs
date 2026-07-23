using System;
using System.IO;
using Serilog;
using Serilog.Formatting.Compact;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// Process-wide Serilog logger for AgentUI. Writes compact-JSON (CLEF) <c>.ndjson</c> that the
    /// in-app log viewer reads back via <c>NdjsonLogReader</c>, one rolling file per day under
    /// <see cref="LogDir"/>, plus a human-readable console line for attached debugging.
    /// <see cref="Initialize"/> is idempotent; <see cref="CloseAndFlush"/> is called on shutdown.
    /// </summary>
    public static class AgentUiLog
    {
        private static readonly object Gate = new();
        private static bool _initialized;

        /// <summary>Directory holding the rolling <c>agentui-*.ndjson</c> files.</summary>
        public static string LogDir => Path.Combine(AgentUiConfig.ConfigDir, "logs");

        /// <summary>The active logger (Serilog's no-op logger until <see cref="Initialize"/> runs).</summary>
        public static ILogger Logger => Log.Logger;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                Directory.CreateDirectory(LogDir);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        new RenderedCompactJsonFormatter(),
                        Path.Combine(LogDir, "agentui-.ndjson"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true)
                    .WriteTo.Console()
                    .CreateLogger();

                _initialized = true;
            }
        }

        public static void CloseAndFlush()
        {
            lock (Gate)
            {
                Log.CloseAndFlush();
                _initialized = false;
            }
        }
    }
}
