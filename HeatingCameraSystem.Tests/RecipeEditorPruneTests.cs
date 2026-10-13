using System;
using System.Collections.Generic;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RecipeEditorPruneTests
    {
        [Fact]
        public void PruneOfflineAgentCameras_RemovesExpiredAndUnknown()
        {
            var online = new List<AgentCameraOption>
            {
                new() { AgentId = "Fresh", CameraIndex = 0 },
                new() { AgentId = "Stale", CameraIndex = 0 },
                new() { AgentId = "Unknown", CameraIndex = 1 },
            };
            DateTime now = DateTime.UtcNow;
            var lastSeen = new Dictionary<(string, int), DateTime>
            {
                [("Fresh", 0)] = now,
                [("Stale", 0)] = now - AgentDirectory.OnlineThreshold - TimeSpan.FromSeconds(1),
            };

            int removed = RecipeEditorViewModel.PruneOfflineAgentCameras(online, lastSeen, now);

            Assert.Equal(2, removed);
            Assert.Single(online);
            Assert.Equal("Fresh", online[0].AgentId);
        }

        [Fact]
        public void PruneStaleTargets_RemovesOfflineKeepsUnassigned()
        {
            var targets = new List<CameraTargetModel>
            {
                new() { AgentId = "Live", CameraIndex = 0 },
                new() { AgentId = "Ghost", CameraIndex = 0 },
                new() { AgentId = string.Empty, CameraIndex = 1 },
            };
            var online = new List<AgentCameraOption>
            {
                new() { AgentId = "Live", CameraIndex = 0 },
            };

            int removed = RecipeEditorViewModel.PruneStaleTargets(targets, online);

            Assert.Equal(1, removed);
            Assert.Equal(2, targets.Count);
            Assert.DoesNotContain(targets, t => t.AgentId == "Ghost");
        }
    }
}
