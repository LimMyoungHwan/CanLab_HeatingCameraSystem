using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class LiveThermalCameraTests
    {
        [Fact]
        public async Task FakeLiveThermalCamera_EmitsFramesUntilStopped()
        {
            using var cam = new FakeLiveThermalCamera();
            using var ready = new ManualResetEventSlim();
            object gate = new();
            int frameCount = 0;
            ThermalFrame? lastFrame = null;

            cam.FrameReady += (_, frame) =>
            {
                lock (gate)
                {
                    lastFrame = frame;
                }

                if (Interlocked.Increment(ref frameCount) >= 3)
                {
                    ready.Set();
                }
            };

            await cam.StartAsync(0);

            Assert.True(ready.Wait(TimeSpan.FromSeconds(2)));

            ThermalFrame? observed;
            lock (gate)
            {
                observed = lastFrame;
            }

            if (observed is null)
            {
                throw new InvalidOperationException("Expected a thermal frame.");
            }

            Assert.Equal(640, observed.Width);
            Assert.Equal(480, observed.Height);
            Assert.Equal(640 * 480, observed.Pixels.Length);

            await cam.StopAsync();
            Assert.False(cam.IsRunning);

            int countAfterStop = Volatile.Read(ref frameCount);
            await Task.Delay(200);

            Assert.Equal(countAfterStop, Volatile.Read(ref frameCount));
        }
    }
}
