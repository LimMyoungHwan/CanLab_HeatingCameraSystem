using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CapturePipelineE2ETests
    {
        [Fact]
        public async Task LiveRunning_SnapshotTee_Persists_ReconstructsLosslessly()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_e2e_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
                int liveFrames = 0;
                runtime.FrameReady += (_, _) => Interlocked.Increment(ref liveFrames);

                await runtime.StartAsync();

                ThermalFrame? snap = await runtime.CaptureSnapshotAsync(
                    maxAge: TimeSpan.FromMilliseconds(500),
                    nextFrameTimeout: TimeSpan.FromSeconds(2));
                Assert.NotNull(snap);

                // Live view is not paused by a capture (tee, not re-open).
                Assert.True(runtime.IsRunning);

                using var index = new LiteDbCaptureIndex(Path.Combine(dir, "idx.db"));
                using var store = new CaptureStore(dir, index);
                CaptureRecord rec = store.Save(snap!, "cam0", cameraIndex: 0, recipeStepId: "step-1");

                ThermalFrame back = ThermalFrameReader.Read(rec);
                Assert.Equal(snap!.Width, back.Width);
                Assert.Equal(snap.Height, back.Height);
                Assert.Equal(snap.Pixels, back.Pixels);

                Assert.True(Volatile.Read(ref liveFrames) > 0);

                await runtime.StopAsync();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
