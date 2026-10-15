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
    public sealed class RecipeMeasurementPeriodTests
    {
        private static RecipeMeasurementRecord Sample(string runId, DateTime at, string name = "R")
            => new() { RunId = runId, RecipeName = name, Timestamp = at };

        [Fact]
        public async Task QueryBetweenAsync_ReturnsHalfOpenRange_Ascending()
        {
            using var db = new LiteDatabase(new MemoryStream());
            var repo = new LiteDbRecipeMeasurementRepository(db);
            var from = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Local);
            var to = from.AddDays(1);

            await repo.InsertAsync(Sample("before", from.AddSeconds(-1)));
            await repo.InsertAsync(Sample("in", from.AddHours(2)));
            await repo.InsertAsync(Sample("in", from));
            await repo.InsertAsync(Sample("after", to));

            var results = (await repo.QueryBetweenAsync(from, to)).ToList();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("in", r.RunId));
            Assert.True(results[0].Timestamp < results[1].Timestamp);
        }

        [Fact]
        public async Task QueryBetweenAsync_ReturnsMoreThanOldHundredRunLimit()
        {
            using var db = new LiteDatabase(new MemoryStream());
            var repo = new LiteDbRecipeMeasurementRepository(db);
            var day = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Local);
            for (int i = 0; i < 150; i++)
                await repo.InsertAsync(Sample($"run{i}", day.AddMinutes(i)));

            var runs = RecipeResultViewModel.BuildRuns(await repo.QueryBetweenAsync(day.Date, day.Date.AddDays(1)));

            Assert.Equal(150, runs.Count);
        }

        [Fact]
        public void BuildRuns_GroupsByRun_NewestFirst_WithEarliestStartAndCount()
        {
            var t = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Local);
            var runs = RecipeResultViewModel.BuildRuns(new[]
            {
                Sample("a", t, "LN30"),
                Sample("a", t.AddMinutes(1), "LN30"),
                Sample("b", t.AddHours(1), "LN10"),
            });

            Assert.Equal(new[] { "b", "a" }, runs.Select(r => r.RunId));
            Assert.Equal(t, runs[1].StartedAt);
            Assert.Equal(2, runs[1].MeasurementCount);
            Assert.Equal("LN30", runs[1].RecipeName);
        }
    }
}
