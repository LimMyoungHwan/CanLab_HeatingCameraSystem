using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CapturePlanEditorViewModelTests
{
    [Fact]
    public void OnlyOnlineCandidatesAppearButSavedOfflineCamerasRemainFlagged() => PlanSta.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-editor-cameras-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
            TemplatePlanTestData.Entries(plan)[0].Cameras = new() { TemplatePlanTestData.Cameras[1].Copy() };
            var recipes = new FileRecipeRepository(root); await recipes.SaveAsync(source);
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db);
            IReadOnlyList<CameraIdentity> online = new[] { TemplatePlanTestData.Cameras[0] };
            using var editor = new CapturePlanEditorViewModel(plan, positions, recipes, () => online);
            var choices = editor.Entries[0].Cameras; Assert.Equal(2, choices.Count); Assert.Single(choices.Where(c => c.IsOnline));
            var offline = Assert.Single(choices.Where(c => !c.IsOnline)); Assert.True(offline.IsSelected); Assert.Contains("오프라인", offline.Label);
            online = Array.Empty<CameraIdentity>(); editor.RefreshCameras();
            Assert.Single(choices); Assert.True(choices[0].IsSelected); Assert.Empty(online);
            Assert.Single(Assert.Single(Assert.IsType<List<CapturePlanEntry>>(editor.Plan.Entries)).Cameras);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public void DraftRecompilesWithCurrentPositionsAndCurrentTemplate() => PlanSta.Run(async () =>
    {
        string root = Path.Combine(Path.GetTempPath(), "hcs-editor-draft-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = TemplatePlanTestData.Template(); var recipes = new FileRecipeRepository(root); await recipes.SaveAsync(source);
            using var db = new LiteDatabase(new MemoryStream()); var positions = new LiteDbCameraPositionRepository(db);
            using var editor = new CapturePlanEditorViewModel(TemplatePlanTestData.Plan(source), positions, recipes, () => TemplatePlanTestData.Cameras);
            Assert.False(editor.TryCompile(out _)); Assert.Contains("공유 위치", editor.ValidationMessage);
            foreach (var position in TemplatePlanTestData.Positions()) await positions.SaveAsync(position);
            await editor.LoadPositionsAsync(); Assert.True(editor.TryCompile(out var first)); Assert.Equal(20, first.Count);
            source.Steps[3].WaitDurationSeconds = 7; await recipes.SaveAsync(source);
            Assert.True(editor.TryCompile(out var second)); Assert.Equal(7, second[3].WaitDurationSeconds); Assert.Equal(1800, first[3].WaitDurationSeconds);
        }
        finally { Directory.Delete(root, true); }
    });
    [Fact]
    public void EntryOrderAndLinksPersistWithoutChangingSourcePlan() => PlanSta.Run(() =>
    {
        var source = TemplatePlanTestData.Template(); var plan = TemplatePlanTestData.Plan(source);
        var repo = new Moq.Mock<HeatingCameraSystem.Core.Interfaces.IRecipeRepository>(); repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { source });
        using var editor = new CapturePlanEditorViewModel(plan, Moq.Mock.Of<HeatingCameraSystem.Core.Interfaces.ICameraPositionRepository>(), repo.Object, () => Array.Empty<CameraIdentity>());
        editor.AddEntryCommand.Execute(null); Assert.Equal(2, editor.Entries.Count); editor.MoveEntryUpCommand.Execute(null);
        Assert.Equal(Assert.IsType<CapturePlanEntryEditor>(editor.SelectedEntry).Model.Key, Assert.IsType<List<CapturePlanEntry>>(editor.Plan.Entries)[0].Key); Assert.Single(TemplatePlanTestData.Entries(plan));
        editor.DeleteEntryCommand.Execute(null); Assert.Single(editor.Entries);
    });
    [Fact]
    public void DirectoryTracksActualAgentAndCameraIndexSeparately()
    {
        var directory = new AgentDirectory(); directory.Note(new AgentStatusMessage { AgentId = "Agent", CameraIndex = 0, CameraStatus = CameraStatus.Connected }); directory.Note(new AgentStatusMessage { AgentId = "Agent", CameraIndex = 2, CameraStatus = CameraStatus.Connected });
        Assert.Equal(new[] { 0, 2 }, directory.OnlineCameras.Select(c => c.CameraIndex));
        Assert.Empty(new AgentDirectory().OnlineCameras); Assert.True(directory.IsCameraOnline(new CameraIdentity { AgentId = "Agent", CameraIndex = 2 }));
    }
}
