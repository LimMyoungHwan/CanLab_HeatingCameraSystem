using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Simulator.Cameras;
using HeatingCameraSystem.Simulator.Config;

namespace HeatingCameraSystem.Tests;

public class LivePreviewColorModeTests
{
    [Fact]
    public void Apply_ColorMode_ReturnsSourceUnchanged()
    {
        LivePreviewColorMode.SetGrayscale(false);
        BitmapSource src = DecodeColorFrame();

        Assert.Same(src, LivePreviewColorMode.Apply(src));
    }

    [Fact]
    public void Apply_GrayscaleMode_ConvertsToFrozenGray8()
    {
        BitmapSource src = DecodeColorFrame();
        try
        {
            LivePreviewColorMode.SetGrayscale(true);
            BitmapSource result = LivePreviewColorMode.Apply(src);

            Assert.NotSame(src, result);
            Assert.Equal(PixelFormats.Gray8, result.Format);
            Assert.True(result.IsFrozen);
            Assert.Equal(src.PixelWidth, result.PixelWidth);
            Assert.Equal(src.PixelHeight, result.PixelHeight);
        }
        finally
        {
            LivePreviewColorMode.SetGrayscale(false);
        }
    }

    private static BitmapSource DecodeColorFrame()
    {
        var scene = new SyntheticThermalScene(new FrameSettings(64, 48), () => 1);
        byte[] jpeg = ThermalPreviewEncoder.EncodeColorJpeg(scene.NextFrame(0));
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(jpeg);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
