using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class HistoryQueryTests
    {
        [Fact]
        public void NormalizeRange_extends_to_end_of_selected_day()
        {
            // WPF DatePicker hands back dates at 00:00:00; the "To" bound must still cover the whole day.
            var from = new DateTime(2026, 8, 19, 0, 0, 0);
            var to = new DateTime(2026, 8, 21, 0, 0, 0);

            var (nFrom, nTo) = HistoryQuery.NormalizeRange(from, to);

            Assert.Equal(new DateTime(2026, 8, 19, 0, 0, 0), nFrom);
            Assert.Equal(new DateTime(2026, 8, 21, 23, 59, 59), nTo);
        }

        [Fact]
        public void NormalizeRange_includes_record_late_on_the_to_day()
        {
            // Regression for the reported bug: a capture on the afternoon of the picked "To" day vanished.
            var (from, to) = HistoryQuery.NormalizeRange(new DateTime(2026, 8, 20), new DateTime(2026, 8, 21));
            var afternoonOfToDay = new DateTime(2026, 8, 21, 17, 30, 0);

            Assert.True(afternoonOfToDay >= from && afternoonOfToDay <= to);
        }

        [Fact]
        public void NormalizeRange_strips_time_on_from_bound()
        {
            var (from, _) = HistoryQuery.NormalizeRange(new DateTime(2026, 8, 20, 9, 15, 30), new DateTime(2026, 8, 21));

            Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0), from);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(1, CaptureSource.Recipe)]
        [InlineData(2, CaptureSource.Manual)]
        [InlineData(3, CaptureSource.AgentUi)]
        [InlineData(-1, null)]
        public void SourceForIndex_maps_combo_index(int index, CaptureSource? expected)
        {
            Assert.Equal(expected, HistoryQuery.SourceForIndex(index));
        }

        [Fact]
        public void ApplyFilters_source_filter_selects_only_matching_source()
        {
            var records = new[]
            {
                new CaptureHistoryRecord { CameraId = "A", Source = CaptureSource.Manual },
                new CaptureHistoryRecord { CameraId = "A", Source = CaptureSource.AgentUi },
                new CaptureHistoryRecord { CameraId = "B", Source = CaptureSource.Manual },
            };

            var result = HistoryQuery.ApplyFilters(records, cameraId: null, source: CaptureSource.Manual).ToList();

            Assert.Equal(2, result.Count);
            Assert.All(result, r => Assert.Equal(CaptureSource.Manual, r.Source));
        }

        [Fact]
        public void ApplyFilters_camera_and_source_both_apply()
        {
            var records = new[]
            {
                new CaptureHistoryRecord { CameraId = "A", Source = CaptureSource.Manual },
                new CaptureHistoryRecord { CameraId = "A", Source = CaptureSource.AgentUi },
                new CaptureHistoryRecord { CameraId = "B", Source = CaptureSource.Manual },
            };

            var result = HistoryQuery.ApplyFilters(records, cameraId: "A", source: CaptureSource.Manual).ToList();

            Assert.Single(result);
            Assert.Equal("A", result[0].CameraId);
        }

        [Fact]
        public void ApplyFilters_no_filters_returns_all()
        {
            var records = new[]
            {
                new CaptureHistoryRecord { CameraId = "A", Source = CaptureSource.Manual },
                new CaptureHistoryRecord { CameraId = "B", Source = CaptureSource.AgentUi },
            };

            var result = HistoryQuery.ApplyFilters(records, cameraId: null, source: null).ToList();

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public async Task Repo_returns_record_late_on_To_day_after_normalization()
        {
            // End-to-end regression for the reported History bug: a capture at 17:00 on the picked
            // "To" day disappeared because the DatePicker's To-bound was 00:00:00 of that day.
            using var db = new LiteDatabase(new MemoryStream());
            var repo = new LiteDbCaptureHistoryRepository(db);
            await repo.InsertAsync(new CaptureHistoryRecord
            {
                CameraId = "Agent_1",
                Source = CaptureSource.Manual,
                Timestamp = new DateTime(2026, 8, 21, 17, 0, 0)
            });

            // DatePicker hands back dates at midnight.
            var rawFrom = new DateTime(2026, 8, 20, 0, 0, 0);
            var rawTo = new DateTime(2026, 8, 21, 0, 0, 0);

            // Raw (buggy) bound excludes the afternoon record...
            var buggy = (await repo.QueryAsync(rawFrom, rawTo, null, 1, int.MaxValue)).ToList();
            Assert.Empty(buggy);

            // ...normalized bound includes it.
            var (from, to) = HistoryQuery.NormalizeRange(rawFrom, rawTo);
            var fixedResult = (await repo.QueryAsync(from, to, null, 1, int.MaxValue)).ToList();
            Assert.Single(fixedResult);
            Assert.Equal("Agent_1", fixedResult[0].CameraId);
        }
    }
}
