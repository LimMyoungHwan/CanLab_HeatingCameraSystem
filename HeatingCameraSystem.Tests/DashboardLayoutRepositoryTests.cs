using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class DashboardLayoutRepositoryTests
    {
        [Fact]
        public async Task SaveForModeAsync_RoundTripsSlots()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbDashboardLayoutRepository(db);
            var slots = new[]
            {
                new DashboardLayoutSlot { Mode = 2, Index = 0, AgentId = "Agent_0", CameraIndex = 1 },
                new DashboardLayoutSlot { Mode = 2, Index = 1 }
            };

            await repo.SaveForModeAsync(2, slots);
            var reloaded = await repo.GetForModeAsync(2);

            Assert.Collection(reloaded,
                slot =>
                {
                    Assert.Equal(2, slot.Mode);
                    Assert.Equal(0, slot.Index);
                    Assert.Equal("Agent_0", slot.AgentId);
                    Assert.Equal(1, slot.CameraIndex);
                },
                slot =>
                {
                    Assert.Equal(2, slot.Mode);
                    Assert.Equal(1, slot.Index);
                    Assert.Null(slot.AgentId);
                    Assert.Null(slot.CameraIndex);
                });
        }

        [Fact]
        public async Task SaveForModeAsync_OverwritesExistingMode()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbDashboardLayoutRepository(db);

            await repo.SaveForModeAsync(3, new[]
            {
                new DashboardLayoutSlot { Mode = 3, Index = 0, AgentId = "Agent_0", CameraIndex = 0 }
            });
            await repo.SaveForModeAsync(3, new[]
            {
                new DashboardLayoutSlot { Mode = 3, Index = 0, AgentId = "Agent_1", CameraIndex = 1 }
            });

            var slot = Assert.Single(await repo.GetForModeAsync(3));
            Assert.Equal("Agent_1", slot.AgentId);
            Assert.Equal(1, slot.CameraIndex);
        }

        [Fact]
        public async Task SaveForModeAsync_IsolatesModes()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbDashboardLayoutRepository(db);

            await repo.SaveForModeAsync(2, new[]
            {
                new DashboardLayoutSlot { Mode = 2, Index = 0, AgentId = "Agent_0", CameraIndex = 0 }
            });

            Assert.Empty(await repo.GetForModeAsync(4));
            Assert.Single(await repo.GetForModeAsync(2));
        }
    }
}
