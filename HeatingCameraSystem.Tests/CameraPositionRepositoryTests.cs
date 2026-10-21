using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class CameraPositionRepositoryTests
{
    [Fact]
    public async Task SameCameraIndexOnDifferentAgentsHasIndependentRolePositions()
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db);
        foreach (var entry in TemplatePlanTestData.Positions()) await repo.SaveAsync(entry);
        var rows = await repo.GetAllAsync(); Assert.Equal(6, rows.Count); Assert.Equal(6, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(0, r.CameraIndex));
        await repo.DeleteAsync(TemplatePlanTestData.Cameras[0], BlackBodyRole.Room); Assert.Equal(5, (await repo.GetAllAsync()).Count);
    }
    [Fact]
    public async Task PointToCoordinateLockUsesSharedDatabaseAndCopiesInputs()
    {
        using var db = new LiteDatabase(new MemoryStream()); var one = new LiteDbCameraPositionRepository(db); var two = new LiteDbCameraPositionRepository(db);
        var entry = TemplatePlanTestData.Positions()[0]; await one.SaveAsync(entry); entry.Position.Kind = CameraPositionKind.Coordinate;
        await Assert.ThrowsAsync<InvalidOperationException>(() => two.SaveAsync(entry));
        var loaded = (await one.GetAllAsync())[0]; loaded.Position.Kind = CameraPositionKind.Coordinate;
        Assert.Equal(CameraPositionKind.Point, (await two.GetAllAsync())[0].Position.Kind);
        await two.DeleteAsync(entry.Camera, entry.Role); await two.SaveAsync(entry); Assert.Equal(CameraPositionKind.Coordinate, (await one.GetAllAsync())[0].Position.Kind);
    }
    [Theory]
    [InlineData(0)][InlineData(1)]
    public async Task ReservedPointsAreRejected(int point)
    {
        using var db = new LiteDatabase(new MemoryStream()); var repo = new LiteDbCameraPositionRepository(db);
        var entry = TemplatePlanTestData.Positions()[0]; entry.Position.PointNumber = point;
        await Assert.ThrowsAnyAsync<ArgumentException>(() => repo.SaveAsync(entry));
    }
    [Fact]
    public async Task LegacyMigrationUsesUniqueRecipeEvidenceAndReportsUnmappedRows()
    {
        using var db = new LiteDatabase(new MemoryStream()); var raw = db.GetCollection<BsonDocument>("camera_positions");
        raw.Insert(new BsonDocument { ["_id"] = "0:1", ["CameraIndex"] = 0, ["BlackBodyIndex"] = 1, ["Position"] = new BsonDocument { ["Kind"] = 1, ["PointNumber"] = 12, ["X"] = 0.0, ["Y"] = 0.0 } });
        raw.Insert(new BsonDocument { ["_id"] = "9:0", ["CameraIndex"] = 9, ["BlackBodyIndex"] = 0, ["Position"] = new BsonDocument { ["Kind"] = 1, ["PointNumber"] = 99, ["X"] = 0.0, ["Y"] = 0.0 } });
        var repo = new LiteDbCameraPositionRepository(db); await repo.MigrateLegacyAsync(new[] { TemplatePlanTestData.Template() });
        var migrated = Assert.Single(await repo.GetAllAsync()); Assert.Equal(BlackBodyRole.Cold, migrated.Role); Assert.Equal(TemplatePlanTestData.Cameras[0].AgentId, migrated.AgentId);
        Assert.Contains("1건", repo.MigrationWarning); Assert.NotNull(raw.FindById("9:0")); Assert.Null(raw.FindById("0:1"));
    }
}
