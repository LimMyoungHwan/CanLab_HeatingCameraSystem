using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    public sealed record BiasOperationResult(bool IsSuccess, int Attempts, string Message, string BiasJson, string FilePath);

    /// <summary>보정과 필수 저장을 순차 실행한다. 최초 시도 후 최대 3회만 다시 시도한다.</summary>
    public static class BiasOperationRunner
    {
        public static async Task<BiasOperationResult> RunAsync(
            Func<CancellationToken, Task> calibrate,
            Func<string?> readBiasJson,
            Func<string, CancellationToken, Task<string>>? persist,
            bool requireFile,
            int attemptTimeoutSeconds,
            string context,
            Action<string>? log,
            CancellationToken cancellationToken)
        {
            int timeout = BiasExecutionPolicy.NormalizeAttemptTimeoutSeconds(attemptTimeoutSeconds);
            string lastError = string.Empty;
            for (int attempt = 1; attempt <= BiasExecutionPolicy.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(timeout));
                var elapsed = Stopwatch.StartNew();
                string stage = "보정";
                WriteLog(log, $"[BIAS] START {context} attempt={attempt}/{BiasExecutionPolicy.MaxAttempts} timeoutSeconds={timeout}");
                try
                {
                    // 취소된 작업을 버리고 새 작업을 시작하지 않는다. 실제 종료를 await한 뒤 재시도한다.
                    await calibrate(attemptCts.Token).ConfigureAwait(false);
                    attemptCts.Token.ThrowIfCancellationRequested();
                    string json = readBiasJson() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(json))
                        throw new InvalidOperationException("이번 BIAS 보정 결과가 없습니다.");

                    string path = string.Empty;
                    if (requireFile)
                    {
                        stage = "bias.json 저장";
                        if (persist is null)
                            throw new InvalidOperationException("BIAS 파일 저장 처리가 연결되지 않았습니다.");
                        path = await persist(json, attemptCts.Token).ConfigureAwait(false);
                        attemptCts.Token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidOperationException("BIAS 파일의 최종 저장 경로를 확인하지 못했습니다.");
                    }

                    string message = $"BIAS OK attempt={attempt}/{BiasExecutionPolicy.MaxAttempts}, elapsedMs={elapsed.ElapsedMilliseconds}, path={path}";
                    WriteLog(log, $"[BIAS] OK {context} {message}");
                    return new BiasOperationResult(true, attempt, message, json, path);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    WriteLog(log, $"[BIAS] CANCEL {context} attempt={attempt}/{BiasExecutionPolicy.MaxAttempts} stage={stage}");
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = attemptCts.IsCancellationRequested
                        ? $"{stage} 제한 시간 {timeout}초 초과 ({ex.Message})"
                        : $"{stage}: {ex.GetType().Name}: {ex.Message}";
                    WriteLog(log, $"[BIAS] FAIL {context} attempt={attempt}/{BiasExecutionPolicy.MaxAttempts} elapsedMs={elapsed.ElapsedMilliseconds} reason={lastError}");
                }

                if (attempt < BiasExecutionPolicy.MaxAttempts)
                    await Task.Delay(BiasExecutionPolicy.RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            string failure = $"NG: BIAS 최초 시도 및 재시도 3회 모두 실패. 마지막 오류: {lastError}";
            WriteLog(log, $"[BIAS] NG {context} attempts={BiasExecutionPolicy.MaxAttempts} reason={lastError}");
            return new BiasOperationResult(false, BiasExecutionPolicy.MaxAttempts, failure, string.Empty, string.Empty);
        }

        private static void WriteLog(Action<string>? log, string message)
        {
            try { log?.Invoke(message); }
            catch (Exception ex) { Debug.WriteLine($"[BIAS] log write failed: {ex.Message}"); }
        }
    }
}
