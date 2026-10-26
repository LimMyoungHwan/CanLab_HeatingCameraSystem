using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class ConsoleCaptureDeepAuditTests
    {
        [Fact]
        public async Task CaptureResultsKeepActualNonzeroCameraIndexSourceAndUniqueShotIds()
        {
            string path = Path.Combine(Path.GetTempPath(), "hcs_console_" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
                var camera = new Mock<ICameraCaptureService>();
                camera.Setup(service => service.CaptureFrame(out path)).Returns(true);
                var nats = new Mock<INatsCommunicationService>();
                var results = new List<CaptureResultMessage>();
                nats.Setup(service => service.PublishCaptureResultAsync(It.IsAny<CaptureResultMessage>()))
                    .Callback<CaptureResultMessage>(results.Add).Returns(Task.CompletedTask);
                var status = new HeatingCameraSystem.Agent.Program.StatusBox(CameraStatus.Connected);

                await HeatingCameraSystem.Agent.Program.HandleCaptureAsync(new CaptureCommandMessage
                {
                    TargetAgentId = "Agent_3", RecipeStepId = "step", Source = CaptureSource.Recipe, ShotCount = 2
                }, camera.Object, nats.Object, "Agent_3", 3, status);

                Assert.Equal(2, results.Count);
                Assert.All(results, result =>
                {
                    Assert.True(result.IsSuccess);
                    Assert.Equal("Agent_3", result.AgentId);
                    Assert.Equal(3, result.CameraIndex);
                    Assert.Equal(CaptureSource.Recipe, result.Source);
                    Assert.Equal("step", result.RecipeStepId);
                    Assert.False(string.IsNullOrWhiteSpace(result.CaptureId));
                });
                Assert.NotEqual(results[0].CaptureId, results[1].CaptureId);
                Assert.Equal(CameraStatus.Connected, status.Current);
            }
            finally { File.Delete(path); }
        }
    }
}
