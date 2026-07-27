using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;

namespace HeatingCameraSystem.Tests;

public class SyntheticThermalSceneTests
{
    private static readonly FrameSettings SmallFrame = new(64, 48);

    [Fact]
    public void NextFrame_HasExpectedDimensions_And14BitPixels()
    {
        var scene = new SyntheticThermalScene(SmallFrame, () => 10);

        var frame = scene.NextFrame(0);

        Assert.Equal(64, frame.Width);
        Assert.Equal(48, frame.Height);
        Assert.Equal(64 * 48, frame.Pixels.Length);
        Assert.All(frame.Pixels, p => Assert.InRange(p, (ushort)0, (ushort)0x3FFF));
    }

    [Fact]
    public void NextFrame_IsDeterministic_ForSameCameraAndTick()
    {
        var scene = new SyntheticThermalScene(SmallFrame, () => 42);

        var a = scene.NextFrame(1);
        var b = scene.NextFrame(1);

        Assert.Equal(a.Pixels, b.Pixels);
        Assert.Equal(a.Timestamp, b.Timestamp);
    }

    [Fact]
    public void NextFrame_Differs_ByCameraAndTick()
    {
        var tick = 1L;
        var scene = new SyntheticThermalScene(SmallFrame, () => tick);

        var camera0 = scene.NextFrame(0);
        var camera1 = scene.NextFrame(1);
        tick = 2;
        var moved = scene.NextFrame(0);

        Assert.NotEqual(camera0.Pixels, camera1.Pixels);
        Assert.NotEqual(camera0.Pixels, moved.Pixels);
    }

    [Fact]
    public void Persist_WritesValidJpeg_AndUniqueSanitizedPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs_synthetic_" + Guid.NewGuid().ToString("N"));
        try
        {
            var scene = new SyntheticThermalScene(SmallFrame, () => 1000);
            var store = new SyntheticCaptureStore(root);

            var first = store.Persist(0, 1, scene.NextFrame(0));
            var second = store.Persist(0, 2, scene.NextFrame(0));

            Assert.True(File.Exists(first.Path));
            Assert.True(File.Exists(second.Path));
            Assert.NotEqual(first.Path, second.Path);
            Assert.StartsWith(root, first.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..", Path.GetFileName(first.Path));
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, first.Bytes.Take(3).ToArray());
            Assert.Equal(first.Bytes, File.ReadAllBytes(first.Path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Persist_UnwritableOutputPath_ThrowsTypedFailure_WithoutPartialFile()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "hcs_capture_blocker_" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(tempFile, "block directory creation");
        try
        {
            var scene = new SyntheticThermalScene(SmallFrame, () => 1000);
            var store = new SyntheticCaptureStore(tempFile);

            Assert.Throws<SyntheticCaptureStoreException>(() => store.Persist(0, 1, scene.NextFrame(0)));
            Assert.Equal("block directory creation", File.ReadAllText(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
