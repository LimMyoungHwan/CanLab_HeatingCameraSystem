using System.Buffers.Binary;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using HeatingCameraSystem.Master.Views;
using ScottPlot.WPF;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class RecipeResultRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionRawHistogramRendersStatisticsWithoutRepresentativeFallback(bool legacyLinked) => PlanSta.Run(async () =>
    {
        string output = Path.Combine(Path.GetTempPath(), legacyLinked ? "HeatingCameraSystem-raw-legacy" : "HeatingCameraSystem-raw-histogram");
        Directory.CreateDirectory(output);
        string rawPath = Path.Combine(output, "BB30_000.raw");
        ushort[] values = { 60000, 100, 200, 300, 400 };
        var bytes = new byte[values.Length * sizeof(ushort)];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * sizeof(ushort)), values[i]);
        await File.WriteAllBytesAsync(rawPath, bytes);

        string[] names = { nameof(AppServices.NatsService), nameof(AppServices.RecipeMeasurementRepo), nameof(AppServices.ImageCacheDir) };
        var services = names.Select(name => typeof(AppServices).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(name)).ToDictionary(property => property, property => property.GetValue(null));
        RecipeResultView? view = null;
        try
        {
            Set(nameof(AppServices.NatsService), null);
            Set(nameof(AppServices.RecipeMeasurementRepo), null);
            Set(nameof(AppServices.ImageCacheDir), Path.Combine(output, "cache"));
            var capture = new CaptureHistoryRecord
            {
                Id = "production-raw-render",
                RunId = "raw-histogram-render",
                CameraId = "Sample production camera",
                AgentId = "SampleAgent",
                AgentRawPath = "representative.y16",
                ProductionRawPath = legacyLinked ? string.Empty : rawPath
            };
            if (legacyLinked)
            {
                string databasePath = Path.Combine(output, "legacy-render-" + Guid.NewGuid().ToString("N") + ".db");
                try
                {
                    using (var database = new LiteDB.LiteDatabase(databasePath))
                    {
                        var repository = new LiteDbCaptureHistoryRepository(database);
                        await repository.InsertAsync(capture);
                        await LegacyRawAssociation.SetOperatorLinkAsync(repository, capture, rawPath, values.Length, 1);
                    }
                    using (var database = new LiteDB.LiteDatabase(databasePath))
                        capture = Assert.Single(await new LiteDbCaptureHistoryRepository(database).QueryByRunAsync(capture.RunId));
                }
                finally { File.Delete(databasePath); }
            }
            // A deliberately different representative makes accidental cache fallback observable.
            string representativePath = Path.Combine(AppServices.ImageCacheDir, "raw", capture.RunId, capture.Id + ".y16");
            Directory.CreateDirectory(Path.GetDirectoryName(representativePath) ?? throw new InvalidOperationException("Cache directory missing."));
            await File.WriteAllBytesAsync(representativePath, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

            var vm = new RecipeResultViewModel();
            view = new RecipeResultView { DataContext = vm };
            AddApplicationResources(view);
            vm.Captures.Add(capture);
            vm.SelectedCapture = capture;
            int[] bins = Array.Empty<int>();
            vm.HistogramLoaded += (_, loaded) => bins = loaded.Bins;
            Layout(view);
            var tabs = Assert.Single(Descendants<TabControl>(view));
            tabs.SelectedItem = Assert.Single(tabs.Items.OfType<TabItem>(), tab => Equals(tab.Header, "히스토그램"));
            Layout(view);
            await vm.LoadHistogramCommand.ExecuteAsync(null);
            Layout(view);

            Assert.Equal($"원본 .raw ({(legacyLinked ? "운영자 지정" : "자동 기록")}): " + rawPath, vm.HistogramSource);
            Assert.Contains("NUC 미보정", vm.HistogramNote);
            Assert.Contains("(0,0) 제외", vm.HistogramNote);
            Assert.Equal($"평균 (Mean): {250.0:F2} · 표준편차 (std): {Math.Sqrt(12500):F2} · 중앙값 (Median): {250.0:F2} · {4:N0} 픽셀", vm.HistogramStatistics);
            Assert.Equal(4, bins.Sum());
            Assert.DoesNotContain(".y16", vm.HistogramSource);
            Assert.Equal("히스토그램 생성 완료", vm.StatusMessage);
            foreach (string text in new[] { vm.HistogramSource, vm.HistogramNote, vm.HistogramStatistics })
                Assert.Contains(Descendants<TextBlock>(view), block => block.Text == text && block.ActualWidth > 0 && block.ActualHeight > 0);
            Assert.Contains(Descendants<WpfPlot>(view), plot => plot.Name == "Histogram" && plot.ActualWidth > 0 && plot.ActualHeight > 0);

            var histogram = Assert.Single(Descendants<WpfPlot>(view), plot => plot.Name == "Histogram");
            Assert.NotEmpty(histogram.Plot.GetPlottables());
            string plotPath = Path.Combine(output, "recipe-result-raw-plot.png");
            histogram.Plot.SavePng(plotPath, (int)histogram.ActualWidth, (int)histogram.ActualHeight);

            // Center line: red dashed vertical at the mean, with the reference-tool legend text.
            var centerLine = Assert.Single(histogram.Plot.GetPlottables().OfType<ScottPlot.Plottables.VerticalLine>());
            Assert.Equal(250.0, centerLine.X);
            Assert.Equal(ScottPlot.Colors.Red, centerLine.Color);
            Assert.Equal(ScottPlot.LinePattern.Dashed, centerLine.LinePattern);
            string expectedLegend = "Center: 250, std:" + Math.Sqrt(12500).ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(expectedLegend, Assert.Single(histogram.Plot.Legend.GetItems()).LabelText);
            Assert.True(histogram.Plot.Legend.IsVisible);
            Assert.Equal("Pixel Intensity", histogram.Plot.Axes.Bottom.Label.Text);
            Assert.Equal("Count", histogram.Plot.Axes.Left.Label.Text);
            string centerDir = Path.Combine(Path.GetTempPath(), "HeatingCameraSystem-hist-center");
            Directory.CreateDirectory(centerDir);
            string centerPng = Path.Combine(centerDir, "histogram-center.png");
            histogram.Plot.SavePng(centerPng, (int)histogram.ActualWidth, (int)histogram.ActualHeight);
            var centerBitmap = new BitmapImage();
            centerBitmap.BeginInit();
            centerBitmap.CacheOption = BitmapCacheOption.OnLoad;
            centerBitmap.UriSource = new Uri(centerPng, UriKind.Absolute);
            centerBitmap.EndInit();
            var centerPixels = new FormatConvertedBitmap(centerBitmap, PixelFormats.Bgra32, null, 0);
            int stride = centerPixels.PixelWidth * 4;
            var pixelData = new byte[stride * centerPixels.PixelHeight];
            centerPixels.CopyPixels(pixelData, stride, 0);
            int lineX = (int)Math.Round(histogram.Plot.GetPixel(new ScottPlot.Coordinates(250, 0)).X);
            int redRows = 0;
            for (int y = 0; y < centerPixels.PixelHeight; y++)
                for (int x = Math.Max(0, lineX - 1); x <= Math.Min(centerPixels.PixelWidth - 1, lineX + 1); x++)
                {
                    int o = y * stride + x * 4;
                    if (pixelData[o + 2] > 200 && pixelData[o + 1] < 60 && pixelData[o] < 60) { redRows++; break; }
                }
            Assert.True(redRows > centerPixels.PixelHeight / 4, $"red dashed line missing at x={lineX}: {redRows} rows");
            var plotBitmap = new BitmapImage();
            plotBitmap.BeginInit();
            plotBitmap.CacheOption = BitmapCacheOption.OnLoad;
            plotBitmap.UriSource = new Uri(plotPath, UriKind.Absolute);
            plotBitmap.EndInit();
            plotBitmap.Freeze();
            // Native Skia content is not captured by RenderTargetBitmap. Composite the actual
            // ScottPlot-rendered bitmap into the same layout for this offscreen artifact only.
            var plotParent = Assert.IsType<DockPanel>(histogram.Parent);
            int plotIndex = plotParent.Children.IndexOf(histogram);
            var plotImage = new Image { Source = plotBitmap, Stretch = Stretch.Fill };
            plotParent.Children.RemoveAt(plotIndex);
            plotParent.Children.Insert(plotIndex, plotImage);
            try
            {
                Layout(view);
                var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, "recipe-result-raw.png"));
                encoder.Save(stream);
                Assert.True(stream.Length > 5000);
            }
            finally
            {
                plotParent.Children.Remove(plotImage);
                plotParent.Children.Insert(plotIndex, histogram);
                Layout(view);
            }

            // An existing invalid production file must fail, never display the valid .y16 cache.
            string invalidPath = Path.Combine(output, "invalid.raw");
            await File.WriteAllBytesAsync(invalidPath, new byte[] { 1 });
            capture.ProductionRawPath = invalidPath;
            capture.LinkedRawPath = string.Empty;
            await vm.LoadHistogramCommand.ExecuteAsync(null);
            Assert.Empty(bins);
            Assert.Empty(vm.HistogramSource);
            Assert.Empty(vm.HistogramStatistics);
            Assert.Contains("히스토그램 읽기 실패", vm.StatusMessage);
            Assert.Empty(histogram.Plot.GetPlottables());
            Assert.Empty(histogram.Plot.Legend.GetItems());
            Assert.False(histogram.Plot.Legend.IsVisible);
        }
        finally
        {
            if (view is not null) view.DataContext = null;
            foreach (var service in services) service.Key.SetValue(null, service.Value);
        }
    });

    [Theory]
    [InlineData("bare")]
    [InlineData("request")]
    [InlineData("malformed")]
    public void LegacyRecipeLookupWithoutMeasurementsUsesParsedStepId(string keyKind) => PlanSta.Run(async () =>
    {
        string output = Path.Combine(Path.GetTempPath(), "raw-step-lookup-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(output, "Sensor_001", "RPP25", "cold");
        Directory.CreateDirectory(folder);
        string rawPath = Path.Combine(folder, "BB20_099.raw");
        await File.WriteAllBytesAsync(rawPath, new byte[] { 255, 255, 4, 0, 6, 0 });
        var step = new RecipeStep
        {
            CameraTargets = new() { new() { AgentId = "Agent_A:Bay_1", CameraIndex = 0,
                TargetChamber = ChamberRange.Mid, TargetBlackBody = BlackBodyRole.Cold } }
        };
        var recipe = new Recipe
        {
            SaveRootPath = output,
            Steps = new() { new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 25 }, step }
        };
        var repository = new Mock<IRecipeRepository>();
        repository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(new[] { recipe });
        string[] names = { nameof(AppServices.RecipeRepo), nameof(AppServices.NatsService), nameof(AppServices.RecipeMeasurementRepo) };
        var services = names.Select(name => typeof(AppServices).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(name)).ToDictionary(property => property, property => property.GetValue(null));
        try
        {
            Set(nameof(AppServices.RecipeRepo), repository.Object);
            Set(nameof(AppServices.NatsService), null);
            Set(nameof(AppServices.RecipeMeasurementRepo), null);
            var capture = new CaptureHistoryRecord
            {
                RunId = "legacy-without-measurements", AgentId = "Agent_A:Bay_1", CameraIndex = 0,
                Timestamp = File.GetLastWriteTimeUtc(rawPath), PixelWidth = 3, PixelHeight = 1,
                RecipeStepId = keyKind switch
                {
                    "bare" => step.StepId,
                    "request" => RecipeCaptureRequestKey.Create(step.StepId, "Agent_A:Bay_1", 0, 2),
                    _ => "not-a-step:Agent_A:Bay_1:0:2"
                }
            };
            var vm = new RecipeResultViewModel { SelectedCapture = capture };

            await vm.LoadHistogramCommand.ExecuteAsync(null);

            if (keyKind == "malformed")
            {
                Assert.Empty(vm.RawCandidates);
                Assert.Empty(vm.HistogramSource);
                Assert.DoesNotContain("히스토그램 읽기 실패", vm.StatusMessage);
            }
            else
            {
                Assert.Equal(rawPath, Assert.Single(vm.RawCandidates).Path);
                Assert.StartsWith("원본 .raw (자동 추정:", vm.HistogramSource);
                Assert.Contains(rawPath, vm.HistogramSource);
                Assert.Contains($"평균 (Mean): {5.0:F2}", vm.HistogramStatistics);
            }
        }
        finally
        {
            foreach (var service in services) service.Key.SetValue(null, service.Value);
            Directory.Delete(output, true);
        }
    });

    private static void Set(string name, object? value) =>
        (typeof(AppServices).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(name)).SetValue(null, value);

    private static void AddApplicationResources(FrameworkElement view)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var app = XDocument.Load(Path.Combine(root, "HeatingCameraSystem.Master", "App.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = app.Root?.Element(presentation + "Application.Resources")
            ?? throw new InvalidOperationException("App resources missing.");
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Elements());
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(dictionary.ToString()));
    }

    private static void Layout(FrameworkElement view)
    {
        Pump();
        view.Measure(new Size(1920, 1080));
        view.Arrange(new Rect(0, 0, 1920, 1080));
        view.UpdateLayout();
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
