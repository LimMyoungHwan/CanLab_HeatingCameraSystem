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
/// Manager E2E driver — fully in-process, no WPF and no console Agent.exe (S7/S8).
///
/// [Range 1] Approval loop: FakeCameraEnumerator finds 2 virtual cameras -> inventory -> the
///   Driver approves each -> AgentId assigned + approval re-published -> manager-state.json persists.
///
/// [Range 2] Redefined runtime IPC (S7): a <see cref="FakeAgentUiRuntime"/> opens both cameras and
///   heartbeats; the Manager marks them running (heartbeat-fresh). Disabling ONE camera makes the
///   Manager publish runtimeUnload for just that camera, so it goes not-running while the other
///   stays running — proving one camera's rejection never drops the others and no process is killed.
///
/// Exit: 0 PASS, 1 verification failed, 2 NATS connect failed, 3 timeout.
/// </summary>
internal static class Program
{
    private static readonly object _invGate = new();
    private static CameraInventoryMessage? _latestInv;
    private static Func<CameraInventoryMessage, bool>? _invPredicate;
    private static TaskCompletionSource<CameraInventoryMessage>? _invWaiter;

    private static async Task<int> Main(string[] args)
    {
        string natsUrl = args.Length > 0 ? args[0] : "nats://127.0.0.1:4222";
        int timeoutSec = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 20;
        const string pcId = "E2E-MGR-PC";

        Console.WriteLine($"[MGR-E2E] NATS = {natsUrl}, timeout = {timeoutSec}s, PCId = {pcId}");

        var installRoot = Path.Combine(Path.GetTempPath(), "HCS_MgrE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Manager"));
        Console.WriteLine($"[MGR-E2E] InstallRoot = {installRoot}");

        var settings = new ManagerSettings
        {
            PCId = pcId,
            NatsUrl = natsUrl,
            SimulateEnumeration = true,
            InstallRoot = installRoot,
        };

        await using var natsMgr = new NatsCommunicationService();
        await using var natsDrv = new NatsCommunicationService();
        await using var natsFake = new NatsCommunicationService();
        try
        {
            await natsMgr.ConnectAsync(natsUrl);
            await natsDrv.ConnectAsync(natsUrl);
            await natsFake.ConnectAsync(natsUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MGR-E2E] FAIL — NATS 연결 실패: {ex.Message}");
            Cleanup(installRoot);
            return 2;
        }
        Console.WriteLine("[MGR-E2E] NATS 연결 완료 (manager + driver + fake-runtime).");

        var store = new ManagerStateStore(installRoot);
        store.Load();
        var supervisor = new AgentSupervisor(settings, store, NullLogger<AgentSupervisor>.Instance, natsMgr);
        var inventory = new InventoryPublisher(natsMgr, settings, store, supervisor, NullLogger<InventoryPublisher>.Instance);
        var cmdHandler = new ManagerCommandHandler(natsMgr, settings, store, supervisor, inventory,
            NullLogger<ManagerCommandHandler>.Instance);
        var enumerator = new FakeCameraEnumerator();

        await natsDrv.SubscribeCameraInventoryAsync(OnInventory);
        cmdHandler.Subscribe();

        // [S7] Manager consumes AgentUI heartbeats -> supervisor liveness + disable reconcile.
        await natsMgr.SubscribeAgentStatusAsync(s => supervisor.NoteHeartbeat(s.AgentId));

        await Task.Delay(500);

        foreach (var cam in enumerator.Enumerate())
        {
            if (store.GetByHardwareId(cam.HardwareId) is null)
            {
                store.Upsert(new CameraEntry
                {
                    HardwareId = cam.HardwareId,
                    OpenCvIndex = cam.OpenCvIndex,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow,
                    IsApproved = false,
                });
            }
        }
        supervisor.SpawnAll();
        await inventory.PublishAsync();
        Console.WriteLine("[MGR-E2E] 초기 inventory 발행 완료.");

        var timeout = TimeSpan.FromSeconds(timeoutSec);

        // ════════════ [Range 1] 승인 루프 ════════════
        Console.WriteLine("[MGR-E2E] [범위 1] 초기 inventory 대기 (2대, 미승인)...");
        var inv = await WaitInventory(m => m.Cameras.Count == 2 && m.Cameras.All(c => !c.IsApproved), timeout);
        if (inv is null)
        {
            Console.Error.WriteLine("[MGR-E2E] FAIL — 초기 inventory 타임아웃.");
            Cleanup(installRoot);
            return 3;
        }
        foreach (var c in inv.Cameras)
            Console.WriteLine($"[MGR-E2E]   발견: hw={c.HardwareId} cvIdx={c.OpenCvIndex}");

        foreach (var c in inv.Cameras)
        {
            string alias = $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   -> Approve hw={c.HardwareId} alias={alias}");
            await natsDrv.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId = pcId,
                Op = ManagerCommandOp.Approve,
                HardwareId = c.HardwareId,
                Payload = alias,
                Timestamp = DateTime.UtcNow,
            });
        }

        Console.WriteLine("[MGR-E2E] [범위 1] 승인 inventory 대기 (AgentId 부여)...");
        var approvedInv = await WaitInventory(
            m => m.Cameras.Count == 2 && m.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId)), timeout);
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
            bool idOk = c.AgentId.StartsWith(prefix) && c.AgentId.Length == prefix.Length + 8;
            bool aliasOk = c.Alias == $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   hw={c.HardwareId} agentId={c.AgentId} alias={c.Alias} idOk={idOk} aliasOk={aliasOk}");
            pass &= c.IsApproved && idOk && aliasOk;
        }

        var statePath = Path.Combine(installRoot, "Manager", "manager-state.json");
        bool stateOk = File.Exists(statePath);
        if (stateOk)
        {
            var persisted = JsonSerializer.Deserialize<ManagerState>(File.ReadAllText(statePath));
            stateOk = persisted is { Cameras.Count: 2 }
                   && persisted.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId));
        }
        Console.WriteLine($"[MGR-E2E]   manager-state.json 영속 & 승인: {stateOk}");
        pass &= stateOk;

        // ════════════ [Range 2] 런타임 IPC (S7 재정의) ════════════
        if (pass)
        {
            Console.WriteLine();
            Console.WriteLine("[MGR-E2E] === [범위 2] 런타임 IPC (S7 재정의) ===");

            var cams = approvedInv.Cameras.Select(c => (Hw: c.HardwareId, AgentId: c.AgentId)).ToList();

            await using var fake = new FakeAgentUiRuntime(natsFake);
            await fake.StartAsync(cams.Select(c => c.AgentId));
            Console.WriteLine($"[MGR-E2E]   FakeAgentUiRuntime 기동 — {cams.Count}대 로드+하트비트.");

            bool bothRunning = await WaitUntilAsync(() => cams.All(c => supervisor.IsRunning(c.Hw)), timeout);
            Console.WriteLine($"[MGR-E2E]   두 카메라 running(heartbeat): {bothRunning}");
            pass &= bothRunning;

            if (bothRunning)
            {
                var target = cams[0];
                var survivor = cams[1];
                Console.WriteLine($"[MGR-E2E]   -> Disable hw={target.Hw} (agent={target.AgentId})");
                await natsDrv.PublishManagerCommandAsync(new ManagerCommandMessage
                {
                    PCId = pcId,
                    Op = ManagerCommandOp.Disable,
                    HardwareId = target.Hw,
                    Timestamp = DateTime.UtcNow,
                });

                bool isolated = await WaitUntilAsync(
                    () => !supervisor.IsRunning(target.Hw) && supervisor.IsRunning(survivor.Hw), timeout);
                await Task.Delay(800);
                bool fakeUnloaded = !fake.IsHeartbeating(target.AgentId);
                bool survivorAlive = fake.IsHeartbeating(survivor.AgentId);

                Console.WriteLine($"[MGR-E2E]   disabled not-running={!supervisor.IsRunning(target.Hw)}, survivor running={supervisor.IsRunning(survivor.Hw)}");
                Console.WriteLine($"[MGR-E2E]   fake: disabled 언로드={fakeUnloaded}, survivor 하트비트={survivorAlive}");
                pass &= isolated && fakeUnloaded && survivorAlive;
            }
        }

        Console.WriteLine();
        Console.WriteLine(pass ? "[MGR-E2E] *** PASS ***" : "[MGR-E2E] *** FAIL ***");

        enumerator.Dispose();
        supervisor.Dispose();
        Cleanup(installRoot);
        return pass ? 0 : 1;
    }

    private static void OnInventory(CameraInventoryMessage msg)
    {
        lock (_invGate)
        {
            _latestInv = msg;
            if (_invPredicate is not null && _invWaiter is not null && _invPredicate(msg))
            {
                _invWaiter.TrySetResult(msg);
                _invPredicate = null;
                _invWaiter = null;
            }
        }
    }

    private static async Task<CameraInventoryMessage?> WaitInventory(
        Func<CameraInventoryMessage, bool> predicate, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<CameraInventoryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_invGate)
        {
            if (_latestInv is not null && predicate(_latestInv)) return _latestInv;
            _invPredicate = predicate;
            _invWaiter = tcs;
        }

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done == tcs.Task) return tcs.Task.Result;

        lock (_invGate) { _invPredicate = null; _invWaiter = null; }
        return null;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (condition()) return true;
            try { await Task.Delay(100, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        return condition();
    }

    private static void Cleanup(string installRoot)
    {
        try { if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true); }
        catch { /* best effort — 테스트 후 임시 디렉터리 정리 */ }
    }
}
