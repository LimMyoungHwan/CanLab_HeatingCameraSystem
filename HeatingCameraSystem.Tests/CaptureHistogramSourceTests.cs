using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests;

public sealed class CaptureHistogramSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hist_source_" + Guid.NewGuid().ToString("N"));

    public CaptureHistogramSourceTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task ProductionRawTakesPrecedenceOverCachedRepresentative()
    {
        string path = Path.Combine(_directory, "BB20_099.raw");
        await File.WriteAllBytesAsync(path, new byte[] { 255, 255, 0x00, 0x13, 0x02, 0x13 });
        var result = await CaptureHistogramSource.LoadAsync(new CaptureHistoryRecord { ProductionRawPath = path },
            () => throw new InvalidOperationException("Must not request corrected .y16"));
        Assert.NotNull(result);
        Assert.Equal("원본 .raw (자동 기록): " + path, result.Source);
        Assert.Equal(2, result.Analysis.PixelCount);
        Assert.Equal(4865, result.Analysis.Mean);
        Assert.Equal(1, result.Analysis.StandardDeviation);
        Assert.Equal(4865, result.Analysis.Median);
        Assert.Equal(2, result.Analysis.Bins.Sum());
    }

    [Fact]
    public async Task PendingTransferFallsBackThenRechecksOnNextLoad()
    {
        string path = Path.Combine(_directory, "BB20_099.raw");
        var capture = new CaptureHistoryRecord { ProductionRawPath = path };
        int requests = 0;
        Task<byte[]?> Representative() { requests++; return Task.FromResult<byte[]?>(new byte[] { 8, 0, 10, 0 }); }
        var missing = await CaptureHistogramSource.LoadAsync(capture, Representative);
        Assert.NotNull(missing);
        Assert.Equal(".y16 (NUC 보정) — 원본 .raw 없음/미전송", missing.Source);
        Assert.Contains("전송 대기", missing.Note);
        Assert.Contains(path, missing.Note);
        Assert.Equal(9, missing.Analysis.Mean);
        await File.WriteAllBytesAsync(path, new byte[] { 255, 255, 4, 0, 6, 0 });
        var transferred = await CaptureHistogramSource.LoadAsync(capture, Representative);
        Assert.NotNull(transferred);
        Assert.Equal(5, transferred.Analysis.Mean);
        Assert.StartsWith("원본 .raw (자동 기록):", transferred.Source);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task WithoutProductionMetadataUsesRepresentativeWithExplanation()
    {
        var result = await CaptureHistogramSource.LoadAsync(new CaptureHistoryRecord(),
            () => Task.FromResult<byte[]?>(new byte[] { 8, 0, 10, 0 }));
        Assert.NotNull(result);
        Assert.Contains("원본 위치 정보 없음(이전 기록)", result.Source);
        Assert.DoesNotContain(".raw 없음", result.Source);
        Assert.Contains("부재를 확인한 것은 아닙니다", result.Note);
        Assert.Equal(2, result.Analysis.PixelCount);
    }

    [Fact]
    public async Task InvalidExistingRawDoesNotSilentlyUseCorrectedData()
    {
        string path = Path.Combine(_directory, "BB20_099.raw");
        await File.WriteAllBytesAsync(path, new byte[] { 255, 255, 4 });
        await Assert.ThrowsAsync<ArgumentException>(() => CaptureHistogramSource.LoadAsync(
            new CaptureHistoryRecord { ProductionRawPath = path },
            () => throw new InvalidOperationException("Must not fall back for an existing corrupt .raw")));
    }
}
