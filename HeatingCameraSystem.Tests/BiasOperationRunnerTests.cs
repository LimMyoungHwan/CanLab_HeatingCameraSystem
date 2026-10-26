using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class BiasOperationRunnerTests
    {
        [Fact]
        public async Task CalibrationFailsThreeTimes_ThenFourthAttemptPersistsBeforeSuccess()
        {
            int calibrations = 0;
            int writes = 0;
            var logs = new List<string>();
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                _ => ++calibrations < 4 ? Task.FromException(new IOException("serial failure")) : Task.CompletedTask,
                () => "current-result",
                (json, _) =>
                {
                    Assert.Equal(4, calibrations);
                    Assert.Equal("current-result", json);
                    writes++;
                    return Task.FromResult("final/bias.json");
                },
                true, 1, "request=test-4", logs.Add, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.Attempts);
            Assert.Equal(4, calibrations);
            Assert.Equal(1, writes);
            Assert.Equal("final/bias.json", result.FilePath);
            Assert.Contains(logs, line => line.Contains("attempt=4/4") && line.Contains("request=test-4"));
        }

        [Fact]
        public async Task CalibrationAlwaysFails_StopsAfterFourAttempts_WithoutWriting()
        {
            int calibrations = 0;
            int writes = 0;
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                _ => { calibrations++; return Task.FromException(new IOException("no frame")); },
                () => "previous-result",
                (_, _) => { writes++; return Task.FromResult("old/bias.json"); },
                true, 1, "failed-calibration", null, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(4, result.Attempts);
            Assert.Equal(4, calibrations);
            Assert.Equal(0, writes);
            Assert.Empty(result.BiasJson);
            Assert.Empty(result.FilePath);
            Assert.Contains("NG", result.Message);
            Assert.Contains("no frame", result.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FileWriteFailure_IsPartOfSameFourAttemptBudget(bool recoverOnFourth)
        {
            int calibrations = 0;
            int writes = 0;
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                _ => { calibrations++; return Task.CompletedTask; },
                () => $"result-{calibrations}",
                (json, _) =>
                {
                    writes++;
                    Assert.Equal($"result-{writes}", json);
                    return recoverOnFourth && writes == 4
                        ? Task.FromResult("verified/bias.json")
                        : Task.FromException<string>(new UnauthorizedAccessException("write denied"));
                },
                true, 1, "failed-file", null, CancellationToken.None);

            Assert.Equal(recoverOnFourth, result.IsSuccess);
            Assert.Equal(4, result.Attempts);
            Assert.Equal(4, calibrations);
            Assert.Equal(4, writes);
            if (!recoverOnFourth) Assert.Contains("write denied", result.Message);
        }

        [Fact]
        public async Task Timeout_EndsEachAttemptBeforeStartingTheNext()
        {
            int running = 0;
            int peak = 0;
            int attempts = 0;
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                async ct =>
                {
                    attempts++;
                    peak = Math.Max(peak, Interlocked.Increment(ref running));
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                    finally { Interlocked.Decrement(ref running); }
                },
                () => "unused", null, false, 1, "timeout", null, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(4, attempts);
            Assert.Equal(1, peak);
            Assert.Equal(0, running);
            Assert.Contains("제한 시간", result.Message);
        }

        [Fact]
        public async Task UserCancellation_DoesNotStartAnotherAttempt()
        {
            using var cts = new CancellationTokenSource();
            int attempts = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BiasOperationRunner.RunAsync(
                ct => { attempts++; cts.Cancel(); return Task.Delay(1, ct); },
                () => "unused", null, false, 1, "cancel", null, cts.Token));
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task SuccessfulHandlerWithoutCurrentJson_CannotReportSuccess()
        {
            int attempts = 0;
            BiasOperationResult result = await BiasOperationRunner.RunAsync(
                _ => { attempts++; return Task.CompletedTask; },
                () => null, null, false, 1, "no-json", null, CancellationToken.None);
            Assert.False(result.IsSuccess);
            Assert.Equal(4, attempts);
            Assert.Contains("보정 결과가 없습니다", result.Message);
        }

        [Fact]
        public void MasterWaitBudget_ContainsEveryAttemptAndRetryDelay()
        {
            TimeSpan work = TimeSpan.FromSeconds(BiasExecutionPolicy.MaxAttempts * 120)
                + TimeSpan.FromMilliseconds((BiasExecutionPolicy.MaxAttempts - 1) * BiasExecutionPolicy.RetryDelayMilliseconds);
            Assert.True(BiasExecutionPolicy.ResponseTimeout(120) > work);
            Assert.Equal(4, BiasExecutionPolicy.MaxAttempts);
        }
    }
}
