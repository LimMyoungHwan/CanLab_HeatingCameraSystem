using System;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraRuntimeManagerTests
    {
        private sealed class ThrowingSource : IThermalFrameSource
        {
            public void Open() => throw new InvalidOperationException("no camera");
            public ThermalFrame? Read() => null;
            public void Close() { }
            public void Dispose() { }
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(15);
            }
            return condition();
        }

        [Fact]
        public async Task StartAll_RunsAllSimCameras_AndExposesFrames()
        {
            using var mgr = new CameraRuntimeManager(
                d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
            mgr.Add(new CameraDescriptor("PC_a", 0, "A"));
            mgr.Add(new CameraDescriptor("PC_b", 1, "B"));
            Assert.Equal(2, mgr.Count);

            await mgr.StartAllAsync();

            Assert.True(await WaitUntilAsync(
                () => mgr.Runtimes.All(r => r.LatestFrame is not null),
                TimeSpan.FromSeconds(2)));
            Assert.All(mgr.Runtimes, r => Assert.True(r.IsRunning));

            await mgr.StopAllAsync();
            Assert.All(mgr.Runtimes, r => Assert.False(r.IsRunning));
        }

        [Fact]
        public async Task StartAll_IsolatesFailingCamera()
        {
            using var mgr = new CameraRuntimeManager(d =>
                d.OpenCvIndex == 99
                    ? new CameraRuntime(d.OpenCvIndex, new ThrowingSource(), framePeriodMs: 10)
                    : new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource(), framePeriodMs: 10));
            mgr.Add(new CameraDescriptor("good", 0, "good"));
            mgr.Add(new CameraDescriptor("bad", 99, "bad"));

            await mgr.StartAllAsync();

            Assert.True(await WaitUntilAsync(
                () => mgr.TryGet("good", out var g) && g.LatestFrame is not null,
                TimeSpan.FromSeconds(2)));
            Assert.True(mgr.TryGet("good", out var good) && good.IsRunning);

            Assert.True(mgr.TryGet("bad", out var bad));
            Assert.True(await WaitUntilAsync(
                () => bad.Status == CameraRuntimeStatus.Faulted,
                TimeSpan.FromSeconds(2)));
            Assert.False(bad.IsRunning);

            await mgr.StopAllAsync();
        }

        [Fact]
        public void Remove_DisposesAndDropsRuntime()
        {
            using var mgr = new CameraRuntimeManager(
                d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()));
            mgr.Add(new CameraDescriptor("x", 0, "x"));
            Assert.Equal(1, mgr.Count);

            mgr.Remove("x");
            Assert.Equal(0, mgr.Count);
            Assert.False(mgr.TryGet("x", out _));
        }
    }
}
