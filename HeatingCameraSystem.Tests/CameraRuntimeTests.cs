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

        [Fact]
        public async Task FrameStarvation_FaultsInsteadOfLookingHealthy()
        {
            var source = new SwitchableFrameSource();
            using var runtime = new CameraRuntime(0, source, framePeriodMs: 5, frameTimeoutMs: 150);
            await runtime.StartAsync();
            await WaitForStatusAsync(runtime, CameraRuntimeStatus.Running);

            source.Starving = true;

            await WaitForStatusAsync(runtime, CameraRuntimeStatus.Faulted);
            await runtime.StopAsync();
        }

        [Fact]
        public async Task FrameStarvation_RecoversToRunningWhenFramesReturn()
        {
            var source = new SwitchableFrameSource { Starving = true };
            using var runtime = new CameraRuntime(0, source, framePeriodMs: 5, frameTimeoutMs: 150);
            await runtime.StartAsync();
            await WaitForStatusAsync(runtime, CameraRuntimeStatus.Faulted);

            source.Starving = false;

            await WaitForStatusAsync(runtime, CameraRuntimeStatus.Running);
            await runtime.StopAsync();
        }

        [Fact]
        public async Task BriefFrameGap_ShorterThanTimeout_StaysRunning()
        {
            var source = new SwitchableFrameSource();
            using var runtime = new CameraRuntime(0, source, framePeriodMs: 5, frameTimeoutMs: 1000);
            await runtime.StartAsync();
            await WaitForStatusAsync(runtime, CameraRuntimeStatus.Running);

            source.Starving = true;
            await Task.Delay(150);

            Assert.Equal(CameraRuntimeStatus.Running, runtime.Status);
            await runtime.StopAsync();
        }

        [Fact]
        public void FakeSource_LoadsRawReplayFrame()
        {
            string path = Path.Combine(Path.GetTempPath(), "hcs_replay_" + Guid.NewGuid().ToString("N") + ".raw");
            var bytes = new byte[640 * 480 * sizeof(ushort)];
            bytes[0] = 0x34;
            bytes[1] = 0x12;
            bytes[^2] = 0xCD;
            bytes[^1] = 0xAB;
            File.WriteAllBytes(path, bytes);

            try
            {
                using var source = new FakeThermalFrameSource(path);

                ThermalFrame frame = Assert.IsType<ThermalFrame>(source.Read());

                Assert.Equal(640, frame.Width);
                Assert.Equal(480, frame.Height);
                Assert.Equal((ushort)0x1234, frame.Pixels[0]);
                Assert.Equal((ushort)0xABCD, frame.Pixels[^1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FakeSource_RejectsWrongRawSize()
        {
            string path = Path.Combine(Path.GetTempPath(), "hcs_replay_bad_" + Guid.NewGuid().ToString("N") + ".raw");
            File.WriteAllBytes(path, new byte[2]);

            try
            {
                Assert.Throws<InvalidDataException>(() => new FakeThermalFrameSource(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static async Task WaitForStatusAsync(CameraRuntime runtime, CameraRuntimeStatus expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (runtime.Status != expected)
            {
                if (cts.IsCancellationRequested)
                    Assert.Fail($"Expected {expected} but stayed {runtime.Status}.");
                await Task.Delay(10);
            }
        }

        private sealed class SwitchableFrameSource : Core.Interfaces.IThermalFrameSource
        {
            private volatile bool _starving;

            public bool Starving
            {
                get => _starving;
                set => _starving = value;
            }

            public void Open() { }

            public ThermalFrame? Read() =>
                _starving ? null : new ThermalFrame(new ushort[4], 2, 2, DateTimeOffset.Now);

            public void Close() { }

            public void Dispose() { }
        }
    }
}
