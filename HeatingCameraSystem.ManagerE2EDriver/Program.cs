using System.Runtime.Versioning;
using System.Text.Json;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.Services;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: SupportedOSPlatform("windows")]

namespace HeatingCameraSystem.ManagerE2EDriver;

/// <summary>
/// SC-12 Manager E2E 드라이버.
///
/// [범위 1] 승인 루프 검증:
///   FakeCameraEnumerator 가상 카메라 2대 발견 → inventory 발행 →
///   Driver가 Approve → AgentId 부여·승인 재발행 → manager-state.json 영속 확인.
///
/// [범위 2] 캡처 Roundtrip 검증 (Agent exe 존재 시 자동 실행):
///   승인 후 Agent.exe spawn(FakeCam 모드) → 하트비트 수신 →
///   capture cmd 발행 → Agent FakeCaptureService 실행 → result 수신 → 검증.
///
/// 종료 코드:
///   0 = PASS, 1 = 검증 실패, 2 = NATS 연결 실패, 3 = 타임아웃
/// </summary>
internal static class Program
{
    // ── Inventory 대기용 공유 상태 ──────────────────────────────────────────────
    // OnInventory 콜백과 WaitInventory 메서드 사이에서 최신 메시지를 공유.
    private static readonly object _invGate = new();
    private static CameraInventoryMessage? _latestInv;
    private static Func<CameraInventoryMessage, bool>? _invPredicate;
    private static TaskCompletionSource<CameraInventoryMessage>? _invWaiter;

    private static async Task<int> Main(string[] args)
    {
        string natsUrl    = args.Length > 0 ? args[0] : "nats://127.0.0.1:4222";
        int    timeoutSec = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 20;
        const string pcId = "E2E-MGR-PC";

        Console.WriteLine($"[MGR-E2E] NATS = {natsUrl}, timeout = {timeoutSec}s, PCId = {pcId}");

        // [SC-12 범위 2] Design Ref: §4.4 — Agent exe 경로 자동 탐지.
        // 세 번째 인수로 직접 지정하거나, 생략 시 FindAgentExe()가 빌드 출력 경로를 탐색.
        // exe가 없으면 범위 1(승인 루프)만 실행, 있으면 범위 2(캡처 roundtrip)까지 실행.
        string agentExePath      = args.Length > 2 ? args[2] : FindAgentExe();
        bool   captureRoundtrip  = File.Exists(agentExePath);

        if (captureRoundtrip)
            Console.WriteLine($"[MGR-E2E] Agent exe 발견 — 캡처 roundtrip 활성화: {agentExePath}");
        else
            Console.WriteLine("[MGR-E2E] Agent exe 없음 — 범위 1(승인 루프)만 실행.");

        var installRoot = Path.Combine(Path.GetTempPath(), "HCS_MgrE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Manager"));
        Console.WriteLine($"[MGR-E2E] InstallRoot = {installRoot}");

        // [SC-12 범위 2] Design Ref: §4.1 — SimulationMode 대신 두 독립 플래그 사용.
        // SimulateEnumeration = true → FakeCameraEnumerator(실 카메라 없이 2대 발견)
        // SimulateAgentMode   = true → Agent.exe 기동 시 FakeCam 모드(실 USB 카메라 불필요)
        // AgentExePath가 존재하면 AgentSupervisor가 실제로 spawn, 없으면 스킵(범위 1 동작).
        var settings = new ManagerSettings
        {
            PCId                = pcId,
            NatsUrl             = natsUrl,
            SimulateEnumeration = true,
            SimulateAgentMode   = true,
            InstallRoot         = installRoot,
            AgentExePath        = agentExePath,
        };

        await using var natsMgr = new NatsCommunicationService();
        await using var natsDrv = new NatsCommunicationService();
        try
        {
            await natsMgr.ConnectAsync(natsUrl);
            await natsDrv.ConnectAsync(natsUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MGR-E2E] FAIL — NATS 연결 실패: {ex.Message}");
            Cleanup(installRoot);
            return 2;
        }
        Console.WriteLine("[MGR-E2E] NATS 연결 완료 (manager + driver 각각).");

        // ── Manager 측 컴포넌트 (실제 AgentManager 클래스, SimulateEnumeration 모드) ──
        var store      = new ManagerStateStore(installRoot);
        store.Load();
        var supervisor = new AgentSupervisor(settings, store, NullLogger<AgentSupervisor>.Instance);
        var inventory  = new InventoryPublisher(natsMgr, settings, store, supervisor,
                             NullLogger<InventoryPublisher>.Instance);
        var cmdHandler = new ManagerCommandHandler(natsMgr, settings, store, supervisor, inventory,
                             NullLogger<ManagerCommandHandler>.Instance);
        var enumerator = new FakeCameraEnumerator();

        // ── 구독 등록 (모든 구독은 명령 실행 전에 설정해야 메시지를 놓치지 않음) ──────
        await natsDrv.SubscribeCameraInventoryAsync(OnInventory);
        cmdHandler.Subscribe();

        // [SC-12 범위 2] 하트비트·캡처 결과 구독을 승인 명령 이전에 등록.
        // 승인 후 Agent.exe가 기동되어 바로 하트비트를 발행할 수 있으므로 미리 대기해야 함.
        var heartbeatsSeen = new HashSet<string>(StringComparer.Ordinal);
        var captureResults = new Dictionary<string, CaptureResultMessage>(StringComparer.Ordinal);
        var sharedLock     = new object();
        // TCS는 승인 완료 후 설정됨. null이면 아직 대기 전임을 의미.
        TaskCompletionSource<bool>? heartbeatTcs = null;
        TaskCompletionSource<bool>? captureTcs   = null;
        int expectedCount = 0;

        await natsDrv.SubscribeAgentStatusAsync(status =>
        {
            // Agent가 NATS 연결 후 5초마다 발행하는 하트비트.
            // Plan SC: SC-01 — 하트비트 수신 확인 후 캡처 커맨드를 안전하게 발행.
            lock (sharedLock)
            {
                if (heartbeatTcs is null) return;
                heartbeatsSeen.Add(status.AgentId);
                Console.WriteLine($"[MGR-E2E]   하트비트: {status.AgentId} cam={status.CameraStatus}");
                if (heartbeatsSeen.Count >= expectedCount)
                    heartbeatTcs.TrySetResult(true);
            }
        });

        await natsDrv.SubscribeCaptureResultAsync(result =>
        {
            // Agent가 FakeCaptureService로 캡처 완료 후 발행하는 결과 메시지.
            // Plan SC: SC-02 — IsSuccess=true + ImageBytes.Length>0 검증 대상.
            lock (sharedLock)
            {
                if (captureTcs is null || captureResults.ContainsKey(result.AgentId)) return;
                captureResults[result.AgentId] = result;
                Console.WriteLine($"[MGR-E2E]   캡처 결과: {result.AgentId} " +
                                  $"success={result.IsSuccess} bytes={result.ImageBytes?.Length ?? 0}");
                if (captureResults.Count >= expectedCount)
                    captureTcs.TrySetResult(true);
            }
        });

        await Task.Delay(500);  // NATS 구독 등록 완료 대기

        // ── Manager 초기화: 가상 카메라 발견 → 미승인 등록 → 초기 inventory 발행 ──────
        foreach (var cam in enumerator.Enumerate())
        {
            if (store.GetByHardwareId(cam.HardwareId) is null)
            {
                store.Upsert(new CameraEntry
                {
                    HardwareId  = cam.HardwareId,
                    OpenCvIndex = cam.OpenCvIndex,
                    FirstSeen   = DateTime.UtcNow,
                    LastSeen    = DateTime.UtcNow,
                    IsApproved  = false,
                });
            }
        }
        supervisor.SpawnAll();          // 승인된 카메라 없음 → spawn 없음
        await inventory.PublishAsync(); // 초기 inventory 발행
        Console.WriteLine("[MGR-E2E] 초기 inventory 발행 완료.");

        var timeout = TimeSpan.FromSeconds(timeoutSec);

        // ════════════════════════════════════════════════════════
        // [범위 1] 승인 루프 검증
        // ════════════════════════════════════════════════════════

        Console.WriteLine("[MGR-E2E] [범위 1] 초기 inventory 대기 (2대, 미승인)...");
        var inv = await WaitInventory(
            m => m.Cameras.Count == 2 && m.Cameras.All(c => !c.IsApproved), timeout);
        if (inv is null)
        {
            Console.Error.WriteLine("[MGR-E2E] FAIL — 초기 inventory 타임아웃.");
            Cleanup(installRoot);
            return 3;
        }
        foreach (var c in inv.Cameras)
            Console.WriteLine($"[MGR-E2E]   발견: hw={c.HardwareId} cvIdx={c.OpenCvIndex} approved={c.IsApproved}");

        // Driver가 Master Devices 탭 역할을 하여 각 카메라를 승인.
        // ManagerCommandHandler가 Approve를 처리하면 AgentId를 부여하고
        // captureRoundtrip=true이면 AgentSupervisor.Spawn()을 실제 호출함.
        foreach (var c in inv.Cameras)
        {
            string alias = $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   -> Approve hw={c.HardwareId} alias={alias}");
            await natsDrv.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = pcId,
                Op         = ManagerCommandOp.Approve,
                HardwareId = c.HardwareId,
                Payload    = alias,
                Timestamp  = DateTime.UtcNow,
            });
        }

        Console.WriteLine("[MGR-E2E] [범위 1] 승인 완료 inventory 대기 (AgentId 부여)...");
        var approvedInv = await WaitInventory(
            m => m.Cameras.Count == 2 && m.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId)),
            timeout);
        if (approvedInv is null)
        {
            Console.Error.WriteLine("[MGR-E2E] FAIL — 승인 inventory 타임아웃.");
            Cleanup(installRoot);
            return 3;
        }

        Console.WriteLine();
        Console.WriteLine("[MGR-E2E] === [범위 1] 승인 루프 VERIFICATION ===");
        bool pass = true;
        string prefix = pcId + "_";
        foreach (var c in approvedInv.Cameras)
        {
            bool idOk    = c.AgentId.StartsWith(prefix) && c.AgentId.Length == prefix.Length + 8;
            bool aliasOk = c.Alias == $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   hw={c.HardwareId} approved={c.IsApproved} " +
                              $"agentId={c.AgentId} alias={c.Alias} idOk={idOk} aliasOk={aliasOk}");
            pass &= c.IsApproved && idOk && aliasOk;
        }

        var statePath = Path.Combine(installRoot, "Manager", "manager-state.json");
        bool stateOk  = File.Exists(statePath);
        if (stateOk)
        {
            var persisted = JsonSerializer.Deserialize<ManagerState>(File.ReadAllText(statePath));
            stateOk = persisted is { Cameras.Count: 2 }
                   && persisted.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId));
        }
        Console.WriteLine($"[MGR-E2E]   manager-state.json 영속 & 승인: {stateOk}");
        pass &= stateOk;

        // ════════════════════════════════════════════════════════
        // [범위 2] 캡처 Roundtrip 검증 (Agent exe 존재 시에만 실행)
        // ════════════════════════════════════════════════════════

        if (captureRoundtrip && pass)
        {
            Console.WriteLine();
            Console.WriteLine("[MGR-E2E] === [범위 2] 캡처 Roundtrip ===");

            // 승인 완료 → ManagerCommandHandler가 supervisor.Spawn() 호출 →
            // Agent.exe가 SimulateAgentMode=True 인수로 기동 → FakeCam 모드.
            // 이제 Agent 하트비트를 기다렸다가 캡처 커맨드를 안전하게 발행.
            expectedCount = approvedInv.Cameras.Count;
            lock (sharedLock)
            {
                heartbeatTcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            Console.WriteLine($"[MGR-E2E] Agent 하트비트 대기 ({expectedCount}대)...");
            var heartbeatDone = await Task.WhenAny(
                heartbeatTcs.Task, Task.Delay(timeout));
            if (heartbeatDone != heartbeatTcs.Task)
            {
                Console.Error.WriteLine(
                    $"[MGR-E2E] FAIL — 하트비트 타임아웃. 수신된 Agent: " +
                    $"{string.Join(", ", heartbeatsSeen)}");
                pass = false;
            }
            else
            {
                // 하트비트 수신 완료 → 캡처 커맨드 발행 준비.
                // Plan SC: SC-02 — 각 Agent에 캡처 커맨드를 발행하고 결과를 수집.
                lock (sharedLock)
                {
                    captureTcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }

                foreach (var c in approvedInv.Cameras)
                {
                    Console.WriteLine($"[MGR-E2E]   -> 캡처 커맨드 → {c.AgentId}");
                    await natsDrv.PublishCaptureCommandAsync(new CaptureCommandMessage
                    {
                        TargetAgentId = c.AgentId,
                        RecipeStepId  = "E2E-1",
                    });
                }

                Console.WriteLine("[MGR-E2E] 캡처 결과 대기 (최대 30초)...");
                var captureDone = await Task.WhenAny(
                    captureTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                if (captureDone != captureTcs.Task)
                {
                    Console.Error.WriteLine(
                        $"[MGR-E2E] FAIL — 캡처 결과 타임아웃. 수신된 Agent: " +
                        $"{string.Join(", ", captureResults.Keys)}");
                    pass = false;
                }
                else
                {
                    Console.WriteLine("[MGR-E2E] === [범위 2] 캡처 VERIFICATION ===");
                    foreach (var (agentId, result) in captureResults)
                    {
                        // IsSuccess=true 이고 ImageBytes가 비어있지 않아야 PASS.
                        // FakeCameraCaptureService는 더미 PNG를 생성해 저장 후 반환함.
                        bool imageOk = result.IsSuccess && result.ImageBytes is { Length: > 0 };
                        Console.WriteLine($"[MGR-E2E]   {agentId}: " +
                                          $"success={result.IsSuccess} " +
                                          $"bytes={result.ImageBytes?.Length ?? 0} ok={imageOk}");
                        pass &= imageOk;
                    }
                }
            }
        }

        // ── 최종 결과 출력 및 정리 ──────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine(pass ? "[MGR-E2E] *** PASS ***" : "[MGR-E2E] *** FAIL ***");

        supervisor.KillAll();
        supervisor.Dispose();
        enumerator.Dispose();
        Cleanup(installRoot);
        return pass ? 0 : 1;
    }

    /// <summary>
    /// Agent.exe 빌드 산출물 경로를 자동으로 탐지한다.
    /// E2EDriver 실행 위치(bin/{cfg}/net8.0/win-x64/)로부터 솔루션 루트를 역산한 뒤
    /// HeatingCameraSystem.Agent 프로젝트의 Debug 또는 Release 빌드 결과를 탐색.
    /// </summary>
    /// <returns>존재하는 Agent.exe 경로, 없으면 빈 문자열.</returns>
    private static string FindAgentExe()
    {
        // [SC-12 범위 2] Design Ref: §4.4 — 솔루션 루트 역산.
        // E2EDriver 출력 경로: HeatingCameraSystem.ManagerE2EDriver/bin/{cfg}/net8.0/win-x64/
        // 5레벨 상위 → HeatingCameraSystem/ (솔루션 루트)
        var baseDir      = AppDomain.CurrentDomain.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

        foreach (var cfg in new[] { "Debug", "Release" })
        {
            var path = Path.Combine(solutionRoot,
                "HeatingCameraSystem.Agent", "bin", cfg, "net8.0",
                "HeatingCameraSystem.Agent.exe");
            if (File.Exists(path)) return path;
        }
        return string.Empty;
    }

    // Inventory 메시지를 수신하여 WaitInventory에서 대기 중인 TCS에 알림.
    private static void OnInventory(CameraInventoryMessage msg)
    {
        lock (_invGate)
        {
            _latestInv = msg;
            if (_invPredicate is not null && _invWaiter is not null && _invPredicate(msg))
            {
                _invWaiter.TrySetResult(msg);
                _invPredicate = null;
                _invWaiter    = null;
            }
        }
    }

    // 조건을 만족하는 inventory 메시지를 비동기로 기다린다.
    // 이미 수신된 최신 메시지가 조건을 만족하면 즉시 반환.
    private static async Task<CameraInventoryMessage?> WaitInventory(
        Func<CameraInventoryMessage, bool> predicate, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<CameraInventoryMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_invGate)
        {
            if (_latestInv is not null && predicate(_latestInv)) return _latestInv;
            _invPredicate = predicate;
            _invWaiter    = tcs;
        }

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done == tcs.Task) return tcs.Task.Result;

        lock (_invGate) { _invPredicate = null; _invWaiter = null; }
        return null;
    }

    private static void Cleanup(string installRoot)
    {
        try { if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true); }
        catch { /* best effort — 테스트 후 임시 디렉터리 정리 */ }
    }
}
