using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Reads Serilog compact-JSON (CLEF) <c>.ndjson</c> log files for the AgentUI log viewer.
    /// Pure parsing (no WPF, no Serilog dependency) so it is unit-testable off the UI thread.
    /// Files are opened with <see cref="FileShare.ReadWrite"/> so the viewer can read a log that
    /// the Serilog file sink is still writing to. Malformed / partial lines are skipped.
    /// </summary>
    public static class NdjsonLogReader
    {
        /// <summary>
        /// Reads entries from a single <c>.ndjson</c> file or every <c>.ndjson</c> in a directory,
        /// keeping only entries at or above <paramref name="minLevel"/>, newest first, capped at
        /// <paramref name="limit"/>.
        /// </summary>
        // ponytail: reads whole files into memory — fine for operator log volumes; add tailing
        // (seek to last N KB) if daily logs ever grow to tens of MB.
        public static IReadOnlyList<LogEntry> Read(
            string logDirOrFile,
            LogEntryLevel minLevel = LogEntryLevel.Verbose,
            int limit = 500)
        {
            var entries = new List<LogEntry>();

            foreach (string file in ResolveFiles(logDirOrFile))
            {
                foreach (string line in SafeReadLines(file))
                {
                    LogEntry? entry = TryParse(line);
                    if (entry is not null && entry.Level >= minLevel)
                    {
                        entries.Add(entry);
                    }
                }
            }

            entries.Sort((a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));
            if (limit > 0 && entries.Count > limit)
            {
                entries.RemoveRange(limit, entries.Count - limit);
            }

            return entries;
        }

        private static IEnumerable<string> ResolveFiles(string logDirOrFile)
        {
            if (string.IsNullOrWhiteSpace(logDirOrFile))
            {
                yield break;
            }

            if (File.Exists(logDirOrFile))
            {
                yield return logDirOrFile;
                yield break;
            }

            if (Directory.Exists(logDirOrFile))
            {
                foreach (string f in Directory.EnumerateFiles(logDirOrFile, "*.ndjson"))
                {
                    yield return f;
                }
            }
        }

        private static IEnumerable<string> SafeReadLines(string file)
        {
            var lines = new List<string>();
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    lines.Add(line);
                }
            }
            catch (IOException)
            {
                // best effort: file locked/rotating — skip this pass
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }

            return lines;
        }

        private static LogEntry? TryParse(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!root.TryGetProperty("@t", out JsonElement t) || t.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                if (!DateTimeOffset.TryParse(
                        t.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset ts))
                {
                    return null;
                }

                LogEntryLevel level = LogEntryLevel.Information;
                if (root.TryGetProperty("@l", out JsonElement l) && l.ValueKind == JsonValueKind.String)
                {
                    level = ParseLevel(l.GetString());
                }

                string message = string.Empty;
                if (root.TryGetProperty("@m", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                {
                    message = m.GetString() ?? string.Empty;
                }
                else if (root.TryGetProperty("@mt", out JsonElement mt) && mt.ValueKind == JsonValueKind.String)
                {
                    message = mt.GetString() ?? string.Empty;
                }

                string? exception = null;
                if (root.TryGetProperty("@x", out JsonElement x) && x.ValueKind == JsonValueKind.String)
                {
                    exception = x.GetString();
                }

                return new LogEntry(ts, level, message, exception);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static LogEntryLevel ParseLevel(string? raw) => raw switch
        {
            "Verbose" => LogEntryLevel.Verbose,
            "Debug" => LogEntryLevel.Debug,
            "Information" => LogEntryLevel.Information,
            "Warning" => LogEntryLevel.Warning,
            "Error" => LogEntryLevel.Error,
            "Fatal" => LogEntryLevel.Fatal,
            _ => LogEntryLevel.Information
        };
    }
}
