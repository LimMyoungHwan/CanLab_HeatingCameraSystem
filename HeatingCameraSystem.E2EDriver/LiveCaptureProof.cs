using System.Collections.Concurrent;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;

namespace HeatingCameraSystem.E2EDriver;

/// <summary>
/// S5 early E2E proof (camera-only, no PLC). Drives an already-running AgentUI with a real
/// camera and proves the integrated path end to end:
///   live view flowing (agent.live.*) → NATS capture cmd → AgentUI tees the live loop →
///   radiometric .y16 written → result published (agent.result.capture.*), while Manager-facing
///   heartbeats (agent.status.*) keep flowing. Every observed event and the final verdict are
///   appended as NDJSON to a log file for a real-run record.
///
/// Fidelity is checked against the real .y16 on disk (this driver runs on the camera PC), using
/// the self-describing sidecar .json for Width/Height. No message-schema change is needed.
/// ponytail: reads .y16 locally; if the driver ever runs off-box, add y16 bytes to the result msg.
/// </summary>
internal static class LiveCaptureProof
{
    private const int ExitPass = 0;
    private const int ExitVerificationFailed = 1;
    private const int ExitNatsConnectFailed = 2;
    private const int ExitTimeout = 3;

    private const int Max14Bit = 0x3FFF;

    private sealed class Observed
    {
        public volatile int LiveFrames;
        public volatile int Heartbeats;
        public DateTime LastLiveUtc;
        public int LastLiveWidth;
        public int LastLiveHeight;
        public string? FirstLiveAgent;
        public string? FirstHeartbeatAgent;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        // args: --live-capture [natsUrl] [agentId] [timeoutSec]
        string natsUrl = args.Length > 1 ? args[1] : "nats://127.0.0.1:4222";
        string wantedAgent = args.Length > 2 ? args[2] : string.Empty;
        int timeoutSec = args.Length > 3 && int.TryParse(args[3], out int t) ? t : 30;

        string logPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            $"e2e-live-capture-{DateTime.Now:yyyyMMdd_HHmmss}.log");
        void Log(string ev, object data)
        {
            string line = JsonSerializer.Serialize(new { ts = DateTime.UtcNow, ev, data });
            Console.WriteLine($"[S5] {ev}: {JsonSerializer.Serialize(data)}");
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { /* log is best effort */ }
        }

        Log("start", new { natsUrl, wantedAgent = wantedAgent.Length == 0 ? "(auto)" : wantedAgent, timeoutSec, logPath });

        await using var nats = new NatsCommunicationService();
        try
        {
            await nats.ConnectAsync(natsUrl);
        }
        catch (Exception ex)
        {
            Log("nats_connect_failed", new { error = ex.Message });
            Console.Error.WriteLine($"[S5] FAIL - NATS connect: {ex.Message}");
            return ExitNatsConnectFailed;
        }
        Log("nats_connected", new { natsUrl });

        var obs = new Observed();
        var gate = new object();
        var results = new ConcurrentDictionary<string, TaskCompletionSource<CaptureResultMessage>>();

        await nats.SubscribeLiveFrameAsync(f =>
        {
            obs.LiveFrames++;
            lock (gate)
            {
                obs.LastLiveUtc = f.Timestamp;
                obs.LastLiveWidth = f.Width;
                obs.LastLiveHeight = f.Height;
                obs.FirstLiveAgent ??= f.AgentId;
            }
        });
        await nats.SubscribeAgentStatusAsync(s =>
        {
            obs.Heartbeats++;
            lock (gate) { obs.FirstHeartbeatAgent ??= s.AgentId; }
        });
        await nats.SubscribeCaptureResultAsync(r =>
        {
            if (results.TryGetValue(r.RecipeStepId, out var tcs)) tcs.TrySetResult(r);
            Log("capture_result", new { r.AgentId, r.RecipeStepId, r.IsSuccess, r.ImagePath, jpegBytes = r.ImageBytes?.Length ?? 0 });
        });

        await Task.Delay(300); // let background subscription loops attach

        // ── Prove live view + Manager-facing heartbeats are actually flowing ──
        Log("await_live_and_heartbeat", new { timeoutSec });
        bool flowing = await WaitUntilAsync(
            () => obs.LiveFrames > 0 && obs.Heartbeats > 0,
            TimeSpan.FromSeconds(timeoutSec));
        if (!flowing)
        {
            Log("no_live_or_heartbeat", new { obs.LiveFrames, obs.Heartbeats });
            Console.Error.WriteLine(
                $"[S5] FAIL - no live/heartbeat (live={obs.LiveFrames}, hb={obs.Heartbeats}). Is AgentUI running with a real camera?");
            return ExitTimeout;
        }

        string agentId;
        DateTime liveRefUtc;
        int liveW, liveH;
        lock (gate)
        {
            agentId = wantedAgent.Length > 0 ? wantedAgent : (obs.FirstLiveAgent ?? obs.FirstHeartbeatAgent ?? "");
            liveRefUtc = obs.LastLiveUtc;
            liveW = obs.LastLiveWidth;
            liveH = obs.LastLiveHeight;
        }
        Log("live_flowing", new { agentId, obs.LiveFrames, obs.Heartbeats, liveRefUtc, liveW, liveH });

        if (agentId.Length == 0)
        {
            Console.Error.WriteLine("[S5] FAIL - could not resolve an AgentId.");
            return ExitVerificationFailed;
        }

        // ── Fire capture at the running AgentUI (it tees the live loop) ──
        string stepId = $"S5-live-{DateTime.Now:HHmmss_fff}";
        var waiter = new TaskCompletionSource<CaptureResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        results[stepId] = waiter;

        Log("publish_capture", new { agentId, stepId });
        await nats.PublishCaptureCommandAsync(new CaptureCommandMessage
        {
            TargetAgentId = agentId,
            RecipeStepId = stepId,
            Timestamp = DateTime.UtcNow,
        });

        var done = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
        if (done != waiter.Task)
        {
            Log("capture_timeout", new { agentId, stepId });
            Console.Error.WriteLine($"[S5] FAIL - capture result timeout for {agentId}.");
            return ExitTimeout;
        }

        CaptureResultMessage result = waiter.Task.Result;

        // ── Verify the real radiometric .y16 on disk ──
        bool pass = VerifyRadiometric(result, liveRefUtc, Log);

        Log("verdict", new { pass, agentId, stepId, obs.LiveFrames, obs.Heartbeats, logPath });
        Console.WriteLine();
        Console.WriteLine(pass ? "[S5] *** PASS ***" : "[S5] *** FAIL ***");
        Console.WriteLine($"[S5] log: {logPath}");
        return pass ? ExitPass : ExitVerificationFailed;
    }

    private static bool VerifyRadiometric(CaptureResultMessage result, DateTime liveRefUtc, Action<string, object> log)
    {
        if (!result.IsSuccess)
        {
            log("verify_fail", new { reason = "result.IsSuccess=false" });
            return false;
        }

        string y16 = result.ImagePath;
        if (string.IsNullOrEmpty(y16) || !y16.EndsWith(".y16", StringComparison.OrdinalIgnoreCase) || !File.Exists(y16))
        {
            log("verify_fail", new { reason = "y16 missing", path = y16 });
            return false;
        }

        string jsonPath = Path.ChangeExtension(y16, ".json");
        if (!File.Exists(jsonPath))
        {
            log("verify_fail", new { reason = "sidecar json missing", path = jsonPath });
            return false;
        }

        CaptureMetadata? meta;
        try { meta = JsonSerializer.Deserialize<CaptureMetadata>(File.ReadAllText(jsonPath)); }
        catch (Exception ex) { log("verify_fail", new { reason = "json parse", error = ex.Message }); return false; }
        if (meta is null || meta.Width <= 0 || meta.Height <= 0)
        {
            log("verify_fail", new { reason = "bad metadata dims" });
            return false;
        }

        long expected = (long)meta.Width * meta.Height * sizeof(ushort);
        long actual = new FileInfo(y16).Length;
        if (actual != expected)
        {
            log("verify_fail", new { reason = "y16 size mismatch", expected, actual, meta.Width, meta.Height });
            return false;
        }

        byte[] raw = File.ReadAllBytes(y16);
        int pixels = raw.Length / sizeof(ushort);
        bool anyNonZero = false;
        int overRange = 0;
        ushort min = ushort.MaxValue, max = ushort.MinValue;
        for (int i = 0; i < pixels; i++)
        {
            ushort v = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8)); // little-endian
            if (v != 0) anyNonZero = true;
            if (v > Max14Bit) overRange++;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        double teeSkewMs = Math.Abs((result.Timestamp - liveRefUtc).TotalMilliseconds);

        log("radiometric", new
        {
            path = y16,
            meta.Width,
            meta.Height,
            meta.PixelFormat,
            sizeBytes = actual,
            min,
            max,
            anyNonZero,
            overRange,
            teeSkewMs,
        });

        if (!anyNonZero) { log("verify_fail", new { reason = "y16 all zero" }); return false; }
        if (overRange > 0) { log("verify_fail", new { reason = "values exceed 14-bit", overRange }); return false; }

        return true;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (condition()) return true;
            try { await Task.Delay(100, cts.Token); } catch (OperationCanceledException) { break; }
        }
        return condition();
    }
}
