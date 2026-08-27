using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// AgentUI 로그 뷰어용 Serilog compact-JSON(CLEF) <c>.ndjson</c> 로그 파일 리더.
    /// 순수 파싱(WPF·Serilog 의존성 없음)이라 UI 스레드 밖에서 단위 테스트할 수 있다.
    /// 파일을 <see cref="FileShare.ReadWrite"/>로 열므로 Serilog 파일 싱크가 아직 쓰고 있는
    /// 로그도 읽을 수 있다. 손상되었거나 불완전한 줄은 건너뛴다.
    /// </summary>
    public static class NdjsonLogReader
    {
        /// <summary>
        /// 단일 <c>.ndjson</c> 파일 또는 디렉터리 안의 모든 <c>.ndjson</c>에서 항목을 읽는다.
        /// <paramref name="minLevel"/> 이상만 남기고, 최신순으로 정렬하며 <paramref name="limit"/>개로
        /// 자른다.
        /// </summary>
        // ponytail: 파일 전체를 메모리로 읽는다 — 운영자 로그 볼륨에는 충분하다. 일일 로그가
        // 수십 MB로 커지면 테일링(마지막 N KB로 seek)을 추가한다.
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
                // 최선 노력: 파일이 잠겼거나 롤링 중 — 이번 패스는 건너뛴다.
            }
            catch (UnauthorizedAccessException)
            {
                // 최선 노력
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
