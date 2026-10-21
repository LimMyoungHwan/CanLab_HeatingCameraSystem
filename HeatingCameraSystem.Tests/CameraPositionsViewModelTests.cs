using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;
using LiteDB;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CameraPositionsViewModelTests
{
    private static CameraPositionsViewModel Create(ICameraPositionRepository repo, IPlcController? plc = null, bool running = false, Func<string, string, bool>? confirm = null)
        => new(() => repo, () => plc, () => 20, () => running, confirm ?? ((_, _) => true), () => TemplatePlanTestData.Cameras);
    [Fact]
    public async Task LoadsOnlyActualCamerasAndThreeRolesIncludingRoom()
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db); var vm = Create(repo);
        await vm.LoadAsync(); Assert.Equal(6, vm.Rows.Count); Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, vm.PointOptions);
        Assert.Contains(vm.Rows, r => r.Role == BlackBodyRole.Room); Assert.All(vm.Rows, r => Assert.Contains(r.CameraLabel, TemplatePlanTestData.Cameras.Select(c => c.Label)));
    }
    [Fact]
    public async Task SavedOfflineIdentityIsRetainedAndPointKindLocked()
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db); await repo.SaveAsync(TemplatePlanTestData.Positions()[0]);
        var vm = new CameraPositionsViewModel(() => repo, () => null, () => 20, () => false, (_, _) => true, () => Array.Empty<CameraIdentity>());
        await vm.LoadAsync(); Assert.Equal(3, vm.Rows.Count);
        var row = vm.Rows.First(r => r.Role == BlackBodyRole.Cold); Assert.True(row.IsKindLocked); row.Kind = CameraPositionKind.Coordinate; Assert.Equal(CameraPositionKind.Point, row.Kind);
        await vm.DeleteCommand.ExecuteAsync(row); Assert.False(row.IsPersisted); row.Kind = CameraPositionKind.Coordinate; await vm.SaveCommand.ExecuteAsync(row); Assert.True(row.IsPersisted);
    }
    [Fact]
    public async Task CurrentCoordinateReadDoesNotMoveAndRunningDisablesMove()
    {
        using var db = new LiteDatabase(new MemoryStream()); var plc = new Mock<IPlcController>(); plc.SetupGet(p => p.IsConnected).Returns(true);
        plc.Setup(p => p.ReadStatusAsync()).ReturnsAsync(new PlcStatusSnapshot { ServoXPosition = 12.3f, ServoYPosition = 45.6f });
        var vm = Create(new LiteDbCameraPositionRepository(db), plc.Object, running: true); await vm.LoadAsync();
        Assert.True(vm.CanUsePlc); Assert.False(vm.CanMove); await vm.FetchCurrentCommand.ExecuteAsync(vm.Rows[0]); Assert.Equal(12.3f, vm.Rows[0].X);
        await vm.TestMoveCommand.ExecuteAsync(vm.Rows[0]); plc.Verify(p => p.MoveToCoordinateAsync(It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }
    [Fact]
    public async Task IdentityFilterAndRegisteredOnlyRespectSavedRows()
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db); await repo.SaveAsync(TemplatePlanTestData.Positions()[0]);
        var vm = Create(repo); await vm.LoadAsync(); vm.FilterText = "Agent_5"; Assert.Equal(3, vm.VisibleRows.Count);
        vm.FilterText = ""; vm.ShowRegisteredOnly = true; Assert.Single(vm.VisibleRows);
    }
}
