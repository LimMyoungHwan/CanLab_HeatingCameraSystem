using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraRuntimeTests
    {
        [Fact]
        public async Task Runtime_ProducesFrames_ExposesLatest_AndStops()
        {
            using var runtime = new CameraRuntime(3, new FakeThermalFrameSource(), framePeriodMs: 10);
            using var ready = new ManualResetEventSlim();
            int count = 0;
            runtime.FrameReady += (_, _) =>
            {
                if (Interlocked.Increment(ref count) >= 3)
                {
                    ready.Set();
                }
            };

            Assert.Equal(3, runtime.CameraIndex);
            Assert.Equal(CameraRuntimeStatus.Stopped, runtime.Status);

            await runtime.StartAsync();
            Assert.True(ready.Wait(TimeSpan.FromSeconds(2)));

            Assert.True(runtime.IsRunning);
            Assert.Equal(CameraRuntimeStatus.Running, runtime.Status);

            ThermalFrame? latest = runtime.LatestFrame;
            Assert.NotNull(latest);
            Assert.Equal(640, latest!.Width);
            Assert.Equal(480, latest.Height);
            Assert.Equal(640 * 480, latest.Pixels.Length);

            await runtime.StopAsync();
            Assert.False(runtime.IsRunning);
            Assert.Equal(CameraRuntimeStatus.Stopped, runtime.Status);

            int afterStop = Volatile.Read(ref count);
            await Task.Delay(150);
            Assert.Equal(afterStop, Volatile.Read(ref count));
        }

        [Fact]
        public async Task CaptureSnapshot_ReturnsTeedFrame_WhileRunning()
        {
            using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);
            await runtime.StartAsync();

            ThermalFrame? snap = await runtime.CaptureSnapshotAsync(
                maxAge: TimeSpan.FromSeconds(1),
                nextFrameTimeout: TimeSpan.FromSeconds(2));

            Assert.NotNull(snap);
            Assert.Equal(640 * 480, snap!.Pixels.Length);

            await runtime.StopAsync();
        }

        [Fact]
        public async Task CaptureSnapshot_BeforeStart_ReturnsNull()
        {
            using var runtime = new CameraRuntime(0, new FakeThermalFrameSource(), framePeriodMs: 10);

            ThermalFrame? snap = await runtime.CaptureSnapshotAsync(
                maxAge: TimeSpan.Zero,
                nextFrameTimeout: TimeSpan.FromMilliseconds(150));

            Assert.Null(snap);
        }
    }
}
