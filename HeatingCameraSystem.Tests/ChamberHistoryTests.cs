using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class ChamberHistoryTests
    {
        [Fact]
        public async Task QueryAsync_ReturnsInRangeRecords_OrderedDescending()
        {
            using var db = new LiteDatabase(new MemoryStream());
            var repo = new LiteDbChamberHistoryRepository(db);

            var t1 = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
            var t2 = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
            var t3 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

            await repo.InsertAsync(new ChamberHistoryRecord { Timestamp = t1, Temperature = 25f, Humidity = 40f, BlackBody1 = 35f, BlackBody2 = 36f });
            await repo.InsertAsync(new ChamberHistoryRecord { Timestamp = t2, Temperature = 26f, Humidity = 41f, BlackBody1 = 35.5f, BlackBody2 = 36.5f });
            await repo.InsertAsync(new ChamberHistoryRecord { Timestamp = t3, Temperature = 27f, Humidity = 42f, BlackBody1 = 36f, BlackBody2 = 37f });

            // Window brackets t1 and t2 only (t3 is days outside — robust to any DateTime.Kind skew).
            var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

            var results = (await repo.QueryAsync(from, to)).ToList();

            Assert.Equal(2, results.Count);
            // OrderByDescending(Timestamp): t2 (26°) precedes t1 (25°).
            Assert.Equal(26f, results[0].Temperature);
            Assert.Equal(25f, results[1].Temperature);
            Assert.Equal(36.5f, results[0].BlackBody2);
            Assert.Equal(2, await repo.CountAsync(from, to));
        }

        [Fact]
        public void ShouldRecord_NullLast_ReturnsTrue()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(ChamberHistoryRecorder.ShouldRecord(null, now, 30));
        }

        [Fact]
        public void ShouldRecord_LastWithinInterval_ReturnsFalse()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(ChamberHistoryRecorder.ShouldRecord(now.AddSeconds(-5), now, 30));
        }

        [Fact]
        public void ShouldRecord_LastOlderThanInterval_ReturnsTrue()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(ChamberHistoryRecorder.ShouldRecord(now.AddSeconds(-31), now, 30));
        }
    }
}
