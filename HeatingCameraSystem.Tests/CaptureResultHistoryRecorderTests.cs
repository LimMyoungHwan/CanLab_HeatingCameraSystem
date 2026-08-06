using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CaptureResultHistoryRecorderTests
    {
        private static CaptureResultMessage Manual(string agentId, string alias, byte[]? bytes = null) => new()
        {
            AgentId = agentId,
            Alias = alias,
            CameraIndex = 2,
            Source = CaptureSource.Manual,
            RecipeStepId = "",
            IsSuccess = true,
            CaptureId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            ImageBytes = bytes
        };

        [Fact]
        public async Task Record_ManualCapture_InsertsOnce_WithAliasKey_AndSnapshotEnrichment()
        {
            var repo = new Mock<ICaptureHistoryRepository>();
            CaptureHistoryRecord? inserted = null;
            repo.Setup(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Callback<CaptureHistoryRecord>(r => inserted = r)
                .Returns(Task.CompletedTask);

            var snap = new PlcStatusSnapshot { CurrentTemperature = 42.5f, CurrentHumidity = 31.0f };
            var recorder = new CaptureResultHistoryRecorder(repo.Object, "", () => snap);

            await recorder.RecordAsync(Manual("Agent_1", "Bay1"));

            repo.Verify(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Once);
            Assert.NotNull(inserted);
            Assert.Equal("Bay1", inserted!.CameraId);
            Assert.Equal("Agent_1", inserted.AgentId);
            Assert.Equal(CaptureSource.Manual, inserted.Source);
            Assert.Equal(42.5f, inserted.Temperature);
            Assert.Equal(31.0f, inserted.Humidity);
        }

        [Fact]
        public async Task Record_EmptyAlias_UsesAgentIdAsKey()
        {
            var repo = new Mock<ICaptureHistoryRepository>();
            CaptureHistoryRecord? inserted = null;
            repo.Setup(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                .Callback<CaptureHistoryRecord>(r => inserted = r)
                .Returns(Task.CompletedTask);
            var recorder = new CaptureResultHistoryRecorder(repo.Object, "", () => null);

            await recorder.RecordAsync(Manual("Agent_9", ""));

            Assert.NotNull(inserted);
            Assert.Equal("Agent_9", inserted!.CameraId);
        }

        [Fact]
        public async Task Record_RecipeCapture_Ignored()
        {
            var repo = new Mock<ICaptureHistoryRepository>();
            var recorder = new CaptureResultHistoryRecorder(repo.Object, "", () => null);

            var recipe = Manual("Agent_1", "Bay1");
            recipe.Source = CaptureSource.Recipe;
            recipe.RecipeStepId = "step1";
            await recorder.RecordAsync(recipe);

            repo.Verify(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Never);
        }

        [Fact]
        public async Task Record_FailedCapture_Ignored()
        {
            var repo = new Mock<ICaptureHistoryRepository>();
            var recorder = new CaptureResultHistoryRecorder(repo.Object, "", () => null);

            var failed = Manual("Agent_1", "Bay1");
            failed.IsSuccess = false;
            await recorder.RecordAsync(failed);

            repo.Verify(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()), Times.Never);
        }

        [Fact]
        public async Task Record_WithImageBytes_CachesAsJpg()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_rec_" + Guid.NewGuid().ToString("N"));
            try
            {
                var repo = new Mock<ICaptureHistoryRepository>();
                CaptureHistoryRecord? inserted = null;
                repo.Setup(r => r.InsertAsync(It.IsAny<CaptureHistoryRecord>()))
                    .Callback<CaptureHistoryRecord>(r => inserted = r)
                    .Returns(Task.CompletedTask);
                var recorder = new CaptureResultHistoryRecorder(repo.Object, dir, () => null);

                await recorder.RecordAsync(Manual("Agent_1", "Bay1", new byte[] { 1, 2, 3, 4 }));

                Assert.NotNull(inserted);
                Assert.EndsWith(".jpg", inserted!.ImagePath);
                Assert.True(File.Exists(inserted.ImagePath));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task Record_ManualCapture_PersistsToLiteDb_AndIsQueryable()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "hcs_caphist_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var db = new LiteDatabase(dbPath);
                var repo = new LiteDbCaptureHistoryRepository(db);
                var recorder = new CaptureResultHistoryRecorder(repo, "",
                    () => new PlcStatusSnapshot { CurrentTemperature = 25f, CurrentHumidity = 50f });

                await recorder.RecordAsync(Manual("FOXSTARSOFTPC_Agent_1", "Bay1"));

                var rows = (await repo.QueryAsync(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5))).ToList();
                Assert.Single(rows);
                Assert.Equal("Bay1", rows[0].CameraId);
                Assert.Equal("FOXSTARSOFTPC_Agent_1", rows[0].AgentId);
                Assert.Equal(CaptureSource.Manual, rows[0].Source);
                Assert.Equal(2, rows[0].CameraIndex);
            }
            finally
            {
                try { File.Delete(dbPath); } catch { /* best effort */ }
            }
        }
    }
}
