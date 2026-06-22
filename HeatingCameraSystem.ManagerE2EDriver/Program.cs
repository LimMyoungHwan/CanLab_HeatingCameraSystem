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
/// SC-12 Manager 승인 루프 E2E 드라이버.
/// 실 NATS 위에서 AgentManager(SimulationMode)를 in-process 호스팅하여
/// FakeEnumerator 카메라 발견 → inventory 발행 → driver Approve → AgentId 부여·승인 재발행
/// 까지의 신규 agent-manager NATS 표면을 검증한다. 캡처 roundtrip은 E2EDriver가 담당.
/// </summary>
internal static class Program
{
    private static readonly object _gate = new();
    private static CameraInventoryMessage? _latest;
    private static Func<CameraInventoryMessage, bool>? _predicate;
    private static TaskCompletionSource<CameraInventoryMessage>? _waiter;

    private static async Task<int> Main(string[] args)
    {
        string natsUrl    = args.Length > 0 ? args[0] : "nats://127.0.0.1:4222";
        int    timeoutSec = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 20;
        const string pcId = "E2E-MGR-PC";

        Console.WriteLine($"[MGR-E2E] NATS = {natsUrl}, timeout = {timeoutSec}s, PCId = {pcId}");

        var installRoot = Path.Combine(Path.GetTempPath(), "HCS_MgrE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(installRoot, "Manager"));
        Console.WriteLine($"[MGR-E2E] InstallRoot = {installRoot}");

        var settings = new ManagerSettings
        {
            PCId           = pcId,
            NatsUrl        = natsUrl,
            SimulationMode = true,
            InstallRoot    = installRoot,
            AgentExePath   = Path.Combine(installRoot, "Agent", "HeatingCameraSystem.Agent.exe"),
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
            Console.Error.WriteLine($"[MGR-E2E] FAIL — NATS connect: {ex.Message}");
            Cleanup(installRoot);
            return 2;
        }
        Console.WriteLine("[MGR-E2E] NATS connected (manager + driver connections).");

        // ── Manager-side components (real AgentManager classes, SimulationMode) ──
        var store      = new ManagerStateStore(installRoot);
        store.Load();
        var supervisor = new AgentSupervisor(settings, store, NullLogger<AgentSupervisor>.Instance);
        var inventory  = new InventoryPublisher(natsMgr, settings, store, supervisor, NullLogger<InventoryPublisher>.Instance);
        var cmdHandler = new ManagerCommandHandler(natsMgr, settings, store, supervisor, inventory, NullLogger<ManagerCommandHandler>.Instance);
        var enumerator = new FakeCameraEnumerator();

        // Driver subscribes to inventory; Manager subscribes to commands.
        await natsDrv.SubscribeCameraInventoryAsync(OnInventory);
        cmdHandler.Subscribe();
        await Task.Delay(500); // let NATS subscriptions register

        // Manager: enumerate fake cameras + register unapproved (mirrors ManagerWorker startup).
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
        supervisor.SpawnAll();              // none approved → no spawn
        await inventory.PublishAsync();     // initial inventory
        Console.WriteLine("[MGR-E2E] Manager published initial inventory.");

        var timeout = TimeSpan.FromSeconds(timeoutSec);

        // 1. Wait for initial inventory: 2 cameras, all unapproved.
        Console.WriteLine("[MGR-E2E] Waiting for inventory (2 cameras, unapproved)...");
        var inv = await WaitInventory(m => m.Cameras.Count == 2 && m.Cameras.All(c => !c.IsApproved), timeout);
        if (inv is null)
        {
            Console.Error.WriteLine("[MGR-E2E] FAIL — initial inventory timeout.");
            Cleanup(installRoot);
            return 3;
        }
        foreach (var c in inv.Cameras)
            Console.WriteLine($"[MGR-E2E]   discovered: hw={c.HardwareId}, cvIdx={c.OpenCvIndex}, approved={c.IsApproved}");

        // 2. Driver approves each camera (acts as Master Devices 탭).
        foreach (var c in inv.Cameras)
        {
            string alias = $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   -> approve hw={c.HardwareId} alias={alias}");
            await natsDrv.PublishManagerCommandAsync(new ManagerCommandMessage
            {
                PCId       = pcId,
                Op         = ManagerCommandOp.Approve,
                HardwareId = c.HardwareId,
                Payload    = alias,
                Timestamp  = DateTime.UtcNow,
            });
        }

        // 3. Wait for inventory: all approved + AgentId assigned.
        Console.WriteLine("[MGR-E2E] Waiting for inventory (all approved, AgentId assigned)...");
        var approvedInv = await WaitInventory(
            m => m.Cameras.Count == 2 && m.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId)),
            timeout);
        if (approvedInv is null)
        {
            Console.Error.WriteLine("[MGR-E2E] FAIL — approval inventory timeout.");
            Cleanup(installRoot);
            return 3;
        }

        // ── VERIFICATION ──
        Console.WriteLine();
        Console.WriteLine("[MGR-E2E] === VERIFICATION ===");
        bool pass = true;
        string prefix = pcId + "_";
        foreach (var c in approvedInv.Cameras)
        {
            bool idOk    = c.AgentId.StartsWith(prefix) && c.AgentId.Length == prefix.Length + 8;
            bool aliasOk = c.Alias == $"E2E-Cam-{c.OpenCvIndex}";
            Console.WriteLine($"[MGR-E2E]   hw={c.HardwareId} approved={c.IsApproved} agentId={c.AgentId} alias={c.Alias} idOk={idOk} aliasOk={aliasOk}");
            pass &= c.IsApproved && idOk && aliasOk;
        }

        // Persisted state check.
        var statePath = Path.Combine(installRoot, "Manager", "manager-state.json");
        bool stateOk = File.Exists(statePath);
        if (stateOk)
        {
            var persisted = JsonSerializer.Deserialize<ManagerState>(File.ReadAllText(statePath));
            stateOk = persisted is { Cameras.Count: 2 }
                   && persisted.Cameras.All(c => c.IsApproved && !string.IsNullOrEmpty(c.AgentId));
        }
        Console.WriteLine($"[MGR-E2E]   manager-state.json persisted & approved: {stateOk}");
        pass &= stateOk;

        Console.WriteLine();
        Console.WriteLine(pass ? "[MGR-E2E] *** PASS ***" : "[MGR-E2E] *** FAIL ***");

        supervisor.KillAll();
        supervisor.Dispose();
        enumerator.Dispose();
        Cleanup(installRoot);
        return pass ? 0 : 1;
    }

    private static void OnInventory(CameraInventoryMessage msg)
    {
        lock (_gate)
        {
            _latest = msg;
            if (_predicate is not null && _waiter is not null && _predicate(msg))
            {
                _waiter.TrySetResult(msg);
                _predicate = null;
                _waiter    = null;
            }
        }
    }

    private static async Task<CameraInventoryMessage?> WaitInventory(
        Func<CameraInventoryMessage, bool> predicate, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<CameraInventoryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_latest is not null && predicate(_latest)) return _latest;
            _predicate = predicate;
            _waiter    = tcs;
        }

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (done == tcs.Task) return tcs.Task.Result;

        lock (_gate) { _predicate = null; _waiter = null; }
        return null;
    }

    private static void Cleanup(string installRoot)
    {
        try { if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true); }
        catch { /* best effort */ }
    }
}
