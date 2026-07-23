using System;
using System.IO;
using System.Linq;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class NdjsonLogReaderTests
    {
        // CLEF (Serilog compact JSON) lines: @t timestamp, optional @l level (absent = Information),
        // @m rendered message, @x exception. Two junk lines must be skipped.
        private static readonly string[] SampleLines =
        {
            """{"@t":"2026-07-23T05:12:30.1000000Z","@m":"AgentUI started"}""",
            """{"@t":"2026-07-23T05:12:31.2000000Z","@l":"Warning","@m":"slow frame"}""",
            """{"@t":"2026-07-23T05:12:32.3000000Z","@l":"Error","@m":"camera faulted","@x":"System.Exception: boom"}""",
            "",
            "this is not json",
            """{"@t":"not-a-timestamp","@m":"bad ts"}"""
        };

        [Fact]
        public void Read_Parses_Fields_Skips_Malformed_And_Orders_NewestFirst()
        {
            string dir = NewTempDir();
            try
            {
                string file = Path.Combine(dir, "agentui-20260723.ndjson");
                File.WriteAllLines(file, SampleLines);

                var entries = NdjsonLogReader.Read(file);

                // 3 valid lines; blank + garbage + bad-timestamp dropped.
                Assert.Equal(3, entries.Count);

                // Newest first.
                Assert.Equal(LogEntryLevel.Error, entries[0].Level);
                Assert.Equal("camera faulted", entries[0].Message);
                Assert.Contains("boom", entries[0].Exception);

                Assert.Equal(LogEntryLevel.Warning, entries[1].Level);

                // Missing @l defaults to Information.
                Assert.Equal(LogEntryLevel.Information, entries[2].Level);
                Assert.Equal("AgentUI started", entries[2].Message);
                Assert.Null(entries[2].Exception);
            }
            finally
            {
                Cleanup(dir);
            }
        }

        [Fact]
        public void Read_Applies_MinLevel_Filter_And_Limit()
        {
            string dir = NewTempDir();
            try
            {
                string file = Path.Combine(dir, "log.ndjson");
                File.WriteAllLines(file, SampleLines);

                var warnPlus = NdjsonLogReader.Read(file, minLevel: LogEntryLevel.Warning);
                Assert.Equal(2, warnPlus.Count);
                Assert.DoesNotContain(warnPlus, e => e.Level == LogEntryLevel.Information);

                var capped = NdjsonLogReader.Read(file, minLevel: LogEntryLevel.Verbose, limit: 1);
                Assert.Single(capped);
                Assert.Equal(LogEntryLevel.Error, capped[0].Level); // newest survives the cap
            }
            finally
            {
                Cleanup(dir);
            }
        }

        [Fact]
        public void Read_Directory_Aggregates_All_Ndjson_Files()
        {
            string dir = NewTempDir();
            try
            {
                File.WriteAllLines(Path.Combine(dir, "a.ndjson"), SampleLines.Take(3));
                File.WriteAllText(
                    Path.Combine(dir, "b.ndjson"),
                    """{"@t":"2026-07-24T00:00:00.0000000Z","@l":"Fatal","@m":"crash"}""" + Environment.NewLine);
                // Non-ndjson file must be ignored.
                File.WriteAllText(Path.Combine(dir, "note.txt"), "ignore me");

                var entries = NdjsonLogReader.Read(dir);

                Assert.Equal(4, entries.Count);
                Assert.Equal(LogEntryLevel.Fatal, entries[0].Level); // 07-24 is newest across files
            }
            finally
            {
                Cleanup(dir);
            }
        }

        [Fact]
        public void Read_Missing_Path_Returns_Empty()
        {
            var entries = NdjsonLogReader.Read(Path.Combine(Path.GetTempPath(), "hcs_nope_" + Guid.NewGuid().ToString("N")));
            Assert.Empty(entries);
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_log_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
