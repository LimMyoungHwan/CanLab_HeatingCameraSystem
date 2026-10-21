using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using HeatingCameraSystem.Master.Views;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class RecipePlanEditorRenderingTests
{
    [Fact]
    public void TemplateEditorBoardAndLegacySlotsRenderWithZeroBindingWarnings() => PlanSta.Run(async () =>
    {
        string output = Path.Combine(Path.GetTempPath(), "HeatingCameraSystem-template-plan-renders"); Directory.CreateDirectory(output);
        string overwriteOutput = Path.Combine(Path.GetTempPath(), "HeatingCameraSystem-overwrite-option");
        string runtime = Path.Combine(output, "runtime-" + Guid.NewGuid().ToString("N"));
        var trace = PresentationTraceSources.DataBindingSource; var oldLevel = trace.Switch.Level; var warnings = new BindingWarnings();
        trace.Switch.Level = SourceLevels.Warning; trace.Listeners.Add(warnings);
        string[] names = { nameof(AppServices.Settings), nameof(AppServices.CameraPositions), nameof(AppServices.NatsService), nameof(AppServices.PlcStatus), nameof(AppServices.RecipeEngine), nameof(AppServices.PlcController), nameof(AppServices.AgentDirectory) };
        var oldServices = names.Select(n => typeof(AppServices).GetProperty(n, BindingFlags.Static | BindingFlags.Public) ?? throw new InvalidOperationException(n)).ToDictionary(p => p, p => p.GetValue(null));
        using var db = new LiteDatabase(new MemoryStream());
        try
        {
            Set(nameof(AppServices.Settings), new HardwareSettings()); Set(nameof(AppServices.NatsService), null); Set(nameof(AppServices.PlcStatus), null); Set(nameof(AppServices.RecipeEngine), null); Set(nameof(AppServices.PlcController), null);
            var directory = new AgentDirectory(); directory.Note(new AgentStatusMessage { AgentId = TemplatePlanTestData.Cameras[0].AgentId, CameraIndex = 0 }); Set(nameof(AppServices.AgentDirectory), directory);
            var recipes = new FileRecipeRepository(runtime); var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
            await recipes.SaveAsync(source); await recipes.SaveAsync(plan);
            var positions = new LiteDbCameraPositionRepository(db); Set(nameof(AppServices.CameraPositions), positions);
            foreach (var row in TemplatePlanTestData.Positions()) await positions.SaveAsync(row);
            using var editor = new CapturePlanEditorViewModel(plan, positions, recipes, () => directory.OnlineCameras); await editor.LoadPositionsAsync();
            var planView = new CapturePlanEditorView { DataContext = editor }; Resources(planView); Render(planView, output, "01-template-plan-editor.png");
            Assert.NotNull(planView.Background);
            Assert.Contains(Descendants<TextBlock>(planView), t => t.Text.StartsWith("1. 챔버") && t.ActualWidth > 0 && t.ActualHeight > 0);
            Assert.True(editor.IsValid, editor.ValidationMessage); Assert.Contains(Descendants<TextBlock>(planView), t => t.Text.Contains("실행 가능"));
            foreach (var camera in TemplatePlanTestData.Cameras)
                Assert.Contains(Descendants<CheckBox>(planView), c => c.Content is TextBlock text && text.Text == camera.Label + (camera.AgentId == directory.OnlineCameras[0].AgentId ? " · 온라인" : " · 오프라인"));
            Assert.Contains(Descendants<TextBlock>(planView), t => t.Text.Contains("대기 30분"));
            Assert.Contains(Descendants<TextBlock>(planView), t => t.Text.Contains("BIAS MID"));
            var runs = new LiteDbCapturePlanRunRepository(db); var service = new CapturePlanRunService(runs, positions, recipes, () => directory.OnlineCameras);
            var prepared = await service.PrepareAsync(plan, CapturePlanRunMode.New);
            prepared.Run.Cells[0].State = CapturePlanCellState.Done; foreach (var step in prepared.Run.Cells[0].Steps) step.Outcome = RecipeStepOutcome.Succeeded;
            prepared.Run.Cells[1].State = CapturePlanCellState.Failed; prepared.Run.Cells[1].Reason = "Cold Capture 실패 · 카메라 절차 전체 재실행 필요";
            prepared.Run.Cells[1].Steps.First(s => s.Kind == RecipeStepKind.CameraCommand && s.Operation == CameraControlOps.Capture).Outcome = RecipeStepOutcome.Failed;
            await runs.SaveAsync(prepared.Run); source.Steps[3].WaitDurationSeconds = 7; await recipes.SaveAsync(source);
            var board = await service.LoadBoardAsync(plan); var boardView = new CapturePlanBoardView { DataContext = board }; Resources(boardView); Render(boardView, output, "02-template-plan-board.png");
            Assert.NotNull(boardView.Background);
            Assert.Contains(Descendants<TextBlock>(boardView), t => t.Text.Contains("변경됨")); Assert.Contains(Descendants<TextBlock>(boardView), t => t.Text.Contains("오프라인")); Assert.Contains(Descendants<TextBlock>(boardView), t => t.Text == "실패");
            using var legacy = new RecipeEditorViewModel(recipes, positions); legacy.SelectRecipeCommand.Execute(legacy.Recipes.Single(r => r.Id == source.Id));
            var legacyView = new RecipeEditorView { DataContext = legacy }; Resources(legacyView); Render(legacyView, output, "03-legacy-template-slots.png");
            Assert.Contains(Descendants<CheckBox>(legacyView), c => Equals(c.Content, "계획 슬롯: 셀 위치")); Assert.Contains(Descendants<CheckBox>(legacyView), c => Equals(c.Content, "계획 슬롯: 셀 카메라"));
            var overwriteBox = Assert.Single(Descendants<CheckBox>(legacyView), c => c.Name == "OverwriteDuplicateShotsCheckBox");
            Assert.Equal("같은 폴더 중복 저장(덮어쓰기)", overwriteBox.Content); Assert.False(overwriteBox.IsChecked); Assert.True(overwriteBox.ActualWidth > 0);
            Assert.Contains("000부터 덮어씁니다", Assert.IsType<string>(overwriteBox.ToolTip)); Assert.Contains("번호를 이어서 저장합니다(기본)", (string)overwriteBox.ToolTip);
            Directory.CreateDirectory(overwriteOutput); Render(legacyView, overwriteOutput, "overwrite-option-unchecked.png");
            Assert.Contains(Descendants<TextBlock>(legacyView), t => t.Name == "CameraTargetSummaryText" && t.Text.Contains(TemplatePlanTestData.Cameras[0].AgentId));
            var cameraStep = Assert.IsType<RecipeModel>(legacy.SelectedRecipe).Steps[4];
            cameraStep.CameraTargets.Clear();
            Render(legacyView, output, "04-legacy-empty-camera.png");
            Assert.Contains(Descendants<TextBlock>(legacyView), t => t.Name == "CameraTargetSummaryText" && ReferenceEquals(t.DataContext, cameraStep) && t.Text == "카메라 선택");
            legacyView.DataContext = null; planView.DataContext = null; boardView.DataContext = null; Pump(); trace.Flush();
            File.WriteAllText(Path.Combine(output, "binding-warnings.txt"), warnings.Text);
            Assert.True(warnings.Count == 0, warnings.Text);
            File.WriteAllText(Path.Combine(output, "render-result.txt"), "STA headless renders: 4 PNGs. Binding warnings: 0. Exact AgentId text and empty selector placeholder asserted. User runtime untouched.");
        }
        finally
        {
            trace.Listeners.Remove(warnings); trace.Switch.Level = oldLevel;
            foreach (var pair in oldServices) pair.Key.SetValue(null, pair.Value);
            if (Directory.Exists(runtime)) Directory.Delete(runtime, true);
        }
    });
    private static void Set(string name, object? value) => (typeof(AppServices).GetProperty(name, BindingFlags.Static | BindingFlags.Public) ?? throw new InvalidOperationException(name)).SetValue(null, value);
    private static void Resources(FrameworkElement view)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var app = XDocument.Load(Path.Combine(root, "HeatingCameraSystem.Master", "App.xaml")); XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = app.Root?.Element(p + "Application.Resources") ?? throw new InvalidOperationException("App resources missing.");
        var dictionary = new XElement(p + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Elements());
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(dictionary.ToString()));
    }
    private static void Render(FrameworkElement view, string folder, string name)
    {
        Pump(); view.Measure(new Size(1920, 1080)); view.Arrange(new Rect(0, 0, 1920, 1080)); view.UpdateLayout(); Pump();
        var image = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32); image.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream); Assert.True(stream.Length > 5000);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class BindingWarnings : TraceListener
    {
        private readonly StringBuilder _text = new(); public int Count { get; private set; } public string Text => _text.ToString();
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) { Count++; _text.Append(message); } }
        public override void WriteLine(string? message) { if (!string.IsNullOrEmpty(message)) { Count++; _text.AppendLine(message); } }
    }
}

internal static class PlanSta
{
    internal static void Run(Func<Task> action) => Run(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Task task = action();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    });
    internal static void Run(Action action)
    {
        Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "STA workflow timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
