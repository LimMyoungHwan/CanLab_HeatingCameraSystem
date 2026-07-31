using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public sealed class AlarmHistoryRepositoryTests
    {
        [Fact]
        public async Task QueryAsync_FiltersDateAndSeverity()
        {
            using var db = new LiteDatabase(new MemoryStream());
            var repository = new LiteDbAlarmHistoryRepository(db);
            var t1 = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Local);
            var t2 = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Local);
            var t3 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Local);

            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = t1, Severity = AlarmSeverity.Info, Source = "PLC", Message = "info" });
            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = t2, Severity = AlarmSeverity.Error, Source = "PLC", Message = "error" });
            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = t3, Severity = AlarmSeverity.Warning, Source = "PLC", Message = "warning" });

            var results = (await repository.QueryAsync(
                new DateTime(2026, 1, 1),
                new DateTime(2026, 1, 6),
                AlarmSeverity.Warning)).ToList();

            Assert.Single(results);
            Assert.Equal("error", results[0].Message);
        }

        [Fact]
        public async Task CountAsync_MatchesSeverityFilter()
        {
            using var db = new LiteDatabase(new MemoryStream());
            var repository = new LiteDbAlarmHistoryRepository(db);
            var timestamp = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Local);

            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = timestamp, Severity = AlarmSeverity.Info });
            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = timestamp.AddMinutes(1), Severity = AlarmSeverity.Warning });
            await repository.InsertAsync(new AlarmHistoryRecord { Timestamp = timestamp.AddMinutes(2), Severity = AlarmSeverity.Error });

            Assert.Equal(2, await repository.CountAsync(
                timestamp.AddHours(-1),
                timestamp.AddHours(1),
                AlarmSeverity.Warning));
        }
    }
}
