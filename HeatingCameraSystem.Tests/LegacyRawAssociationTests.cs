using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests;

public sealed class LegacyRawAssociationTests : IDisposable
{
    private const string CaptureStepId = "5264cf2f-e425-45a0-9322-fc09b485d0b3";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raw_legacy_" + Guid.NewGuid().ToString("N"));
    private readonly CaptureHistoryRecord _capture = new()
    {
        RunId = "legacy-run", RecipeStepId = CaptureStepId, AgentId = "Agent_A", CameraIndex = 0,
        Timestamp = DateTime.UtcNow, Temperature = 40, PixelWidth = 3, PixelHeight = 1
    };

    public LegacyRawAssociationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private Recipe Recipe() => new()
    {
        SaveRootPath = _root,
        Steps = new()
        {
            new() { Kind = RecipeStepKind.ChamberControl, TargetChamberTemperature = 25 },
            new() { StepId = CaptureStepId, CameraTargets = new() { new() { AgentId = "Agent_A", CameraIndex = 0,
                TargetChamber = ChamberRange.Mid, TargetBlackBody = BlackBodyRole.Cold } } }
        }
    };

    private async Task<string> Raw(string product, double seconds = 0, string condition = "RPP25", string name = "BB20_099.raw")
    {
        string folder = Path.Combine(_root, product, condition, "cold");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, new byte[] { 255, 255, 4, 0, 6, 0 });
        File.SetLastWriteTimeUtc(path, _capture.Timestamp.AddSeconds(seconds));
        return path;
    }

    [Theory]
    [InlineData("Agent_A")]
    [InlineData("Agent_A:Bay_1")]
    public async Task EngineRequestKeyFindsStepFolderAndAutomaticallyMatchesRaw(string agentId)
    {
        var recipe = Recipe();
        recipe.Steps[1].CameraTargets[0].AgentId = agentId;
        _capture.AgentId = agentId;
        _capture.RecipeStepId = RecipeCaptureRequestKey.Create(CaptureStepId, agentId, 0, 3);
        Assert.Equal($"{CaptureStepId}:{agentId}:0:3", _capture.RecipeStepId);
        string path = await Raw("Sensor_001", 0.5);

        var context = LegacyRawAssociation.FromRecipe(_capture, recipe);

        Assert.NotNull(context);
        Assert.Equal("RPP25", context.ConditionFolder);
        Assert.Equal("cold", context.RoleFolder);
        Assert.Equal("BB20", context.FilePrefix);
        Assert.Equal(path, LegacyRawAssociation.Discover(_capture, context).Automatic?.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":Agent_A:0:0")]
    [InlineData("not-a-step:Agent_A:0:0")]
    [InlineData("capture_extra:Agent_A:0:0")]
    public async Task MalformedRequestKeyDoesNotMatchOrAutomaticallyLink(string? requestKey)
    {
        await Raw("Sensor_001");
        _capture.RecipeStepId = requestKey ?? string.Empty;
        Assert.Null(RecipeCaptureRequestKey.GetStepId(requestKey));

        var context = LegacyRawAssociation.FromRecipe(_capture, Recipe());

        Assert.NotNull(context);
        Assert.Null(context.ConditionFolder);
        Assert.Null(LegacyRawAssociation.Discover(_capture, context).Automatic);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("capture:Agent_A:Bay_1:0:3")]
    public void KnownLegacyNonGuidStepIdIsAcceptedExactly(string requestKey)
    {
        var steps = new[] { new RecipeStep { StepId = "capture" } };
        Assert.Null(RecipeCaptureRequestKey.GetStepId(requestKey));
        Assert.Equal("capture", RecipeCaptureRequestKey.GetStepId(requestKey, steps));
        Assert.Null(RecipeCaptureRequestKey.GetStepId("capture_extra:Agent_A:0:0", steps));
    }

    [Fact]
    public async Task LegacyUniqueFolderAndTightTimeUsesRawWithoutRepresentative()
    {
        string path = await Raw("Sensor_001", 0.5);
        var context = LegacyRawAssociation.FromRecipe(_capture, Recipe());
        Assert.NotNull(context);
        Assert.Equal("RPP25", context.ConditionFolder); // Target, not measured 40°C.
        var discovery = LegacyRawAssociation.Discover(_capture, context);
        Assert.Equal(path, discovery.Automatic?.Path);
        var result = await CaptureHistogramSource.LoadAsync(_capture,
            () => throw new InvalidOperationException("Corrected representative must not be read"), discovery);
        Assert.NotNull(result);
        Assert.Contains("원본 .raw (자동 추정:", result.Source);
        Assert.Contains("±2초", result.Source);
        Assert.Equal(5, result.Analysis.Mean);
        Assert.Equal(2, result.Analysis.PixelCount);
    }

    [Fact]
    public async Task AmbiguousFoldersRemainCandidatesAndDoNotClaimAbsence()
    {
        await Raw("Sensor_001");
        await Raw("Sensor_002", 0.1);
        var discovery = LegacyRawAssociation.Discover(_capture, LegacyRawAssociation.FromRecipe(_capture, Recipe()));
        Assert.Null(discovery.Automatic);
        Assert.Equal(2, discovery.Candidates.Count);
        var result = await CaptureHistogramSource.LoadAsync(_capture,
            () => Task.FromResult<byte[]?>(new byte[] { 10, 0, 20, 0 }), discovery);
        Assert.NotNull(result);
        Assert.Contains("원본 위치 정보 없음(이전 기록)", result.Source);
        Assert.DoesNotContain(".raw 없음", result.Source);
        Assert.Equal(15, result.Analysis.Mean);
    }

    [Theory]
    [InlineData(2.001)]
    [InlineData(-2.001)]
    public async Task OutsideTightWindowNeverAutomaticallyLinks(double seconds)
    {
        await Raw("Sensor_001", seconds);
        var discovery = LegacyRawAssociation.Discover(_capture, LegacyRawAssociation.FromRecipe(_capture, Recipe()));
        Assert.Null(discovery.Automatic);
        Assert.Single(discovery.Candidates);
    }

    [Fact]
    public async Task EqualTimeFilesInOneFolderRemainAmbiguous()
    {
        await Raw("Sensor_001", name: "BB20_098.raw");
        await Raw("Sensor_001", name: "BB20_099.raw");
        Assert.Null(LegacyRawAssociation.Discover(_capture, LegacyRawAssociation.FromRecipe(_capture, Recipe())).Automatic);
    }

    [Fact]
    public async Task FolderMatchRanksBeforeTimeAndUnknownCameraCannotAutoLink()
    {
        string correct = await Raw("Sensor_001", 1);
        await Raw("Sensor_001", 0, "RPP40");
        var discovery = LegacyRawAssociation.Discover(_capture, LegacyRawAssociation.FromRecipe(_capture, Recipe()));
        Assert.Equal(correct, discovery.Candidates[0].Path);
        _capture.AgentId = "DifferentAgent";
        Assert.Null(LegacyRawAssociation.Discover(_capture, LegacyRawAssociation.FromRecipe(_capture, Recipe())).Automatic);
    }

    [Fact]
    public async Task OperatorLinkPersistsAcrossReopenWinsAndCanChangeOrClear()
    {
        string linked = await Raw("Sensor_001");
        string recorded = await Raw("Sensor_002");
        await File.WriteAllBytesAsync(recorded, new byte[] { 255, 255, 20, 0, 40, 0 });
        _capture.ProductionRawPath = recorded;
        string databasePath = Path.Combine(_root, "history.db");
        using (var database = new LiteDatabase(databasePath))
        {
            var repository = new LiteDbCaptureHistoryRepository(database);
            await repository.InsertAsync(_capture);
            await LegacyRawAssociation.SetOperatorLinkAsync(repository, _capture, linked, 3, 1);
        }
        using (var database = new LiteDatabase(databasePath))
        {
            var repository = new LiteDbCaptureHistoryRepository(database);
            var capture = Assert.Single(await repository.QueryByRunAsync(_capture.RunId));
            Assert.Equal(linked, capture.LinkedRawPath);
            Assert.Equal(3, capture.PixelWidth);
            var result = await CaptureHistogramSource.LoadAsync(capture, () => throw new InvalidOperationException());
            Assert.NotNull(result);
            Assert.Contains("운영자 지정", result.Source);
            Assert.Equal(5, result.Analysis.Mean);
            await LegacyRawAssociation.SetOperatorLinkAsync(repository, capture, recorded, 3, 1);
            Assert.Equal(recorded, Assert.Single(await repository.QueryByRunAsync(capture.RunId)).LinkedRawPath);
            await LegacyRawAssociation.SetOperatorLinkAsync(repository, capture, string.Empty, 3, 1);
        }
        using (var database = new LiteDatabase(databasePath))
        {
            var capture = Assert.Single(await new LiteDbCaptureHistoryRepository(database).QueryByRunAsync(_capture.RunId));
            Assert.Empty(capture.LinkedRawPath);
            var result = await CaptureHistogramSource.LoadAsync(capture, () => throw new InvalidOperationException());
            Assert.NotNull(result);
            Assert.Contains("자동 기록", result.Source);
            Assert.Equal(30, result.Analysis.Mean);
        }
    }

    [Fact]
    public async Task InvalidOrUnreadableSelectionCannotOverwritePersistedLink()
    {
        string valid = await Raw("Sensor_001");
        string invalid = Path.Combine(_root, "invalid.raw");
        await File.WriteAllBytesAsync(invalid, new byte[] { 1, 2, 3, 4 });
        using var database = new LiteDatabase(new MemoryStream());
        var repository = new LiteDbCaptureHistoryRepository(database);
        await repository.InsertAsync(_capture);
        await LegacyRawAssociation.SetOperatorLinkAsync(repository, _capture, valid, 3, 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => LegacyRawAssociation.SetOperatorLinkAsync(repository, _capture, invalid, 3, 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => LegacyRawAssociation.SetOperatorLinkAsync(repository, _capture, valid, 0, 0));
        using (var locked = new FileStream(valid, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => LegacyRawAssociation.SetOperatorLinkAsync(repository, _capture, valid, 3, 1));
        Assert.Equal(valid, Assert.Single(await repository.QueryByRunAsync(_capture.RunId)).LinkedRawPath);
    }
}
