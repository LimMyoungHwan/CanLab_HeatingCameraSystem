using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.Services;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: SupportedOSPlatform("windows")]

var builder = Host.CreateApplicationBuilder(args);

// ── 설정 ─────────────────────────────────────────────────────────────────────
var installRoot  = args.Length > 0 ? args[0] : @"C:\HeatingCameraSystem";
var settingsPath = Path.Combine(installRoot, "Manager", "manager-settings.json");
var settings = File.Exists(settingsPath)
    ? JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(settingsPath)) ?? new ManagerSettings()
    : new ManagerSettings();
settings.InstallRoot = installRoot;

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<ManagerStateStore>(sp =>
{
    var store = new ManagerStateStore(installRoot);
    store.Load();
    return store;
});

// ── NATS ─────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<INatsCommunicationService, NatsCommunicationService>();

// ── 카메라 열거기 ─────────────────────────────────────────────────────────────
// [SC-12 범위 2] Design Ref: §4.2 — SimulationMode → SimulateEnumeration.
// SimulateEnumeration=true 이면 실 카메라 없이 가상 카메라 2대를 반환하는 FakeCameraEnumerator 사용.
// false 이면 WMI로 실제 연결된 USB 카메라를 탐지하는 WmiCameraEnumerator 사용.
builder.Services.AddSingleton<ICameraEnumerator>(sp =>
    settings.SimulateEnumeration
        ? (ICameraEnumerator)new FakeCameraEnumerator()
        : new WmiCameraEnumerator());

// ── 서비스 ────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AgentSupervisor>();
builder.Services.AddSingleton<InventoryPublisher>();
builder.Services.AddSingleton<LogTailService>();
builder.Services.AddSingleton<LogDumpHandler>();
builder.Services.AddSingleton<ManagerCommandHandler>();
builder.Services.AddHostedService<ManagerWorker>();

var host = builder.Build();
await host.RunAsync();

// ── 워커 ──────────────────────────────────────────────────────────────────────

/// <summary>
/// AgentManager의 메인 백그라운드 워커. NATS 연결 후 명령/로그 덤프 구독을 걸고,
/// 카메라를 열거해 상태 저장소와 병합하며, 승인된 카메라의 런타임 로드를 요청하고
/// PnP 변경을 감시한다. 인벤토리는 <c>agent-mgr.inventory.{PCId}</c>로 발행한다.
/// </summary>
public class ManagerWorker : BackgroundService
{
    private readonly INatsCommunicationService _nats;
    private readonly ManagerSettings _settings;
    private readonly ManagerStateStore _store;
    private readonly ICameraEnumerator _enumerator;
    private readonly AgentSupervisor _supervisor;
    private readonly InventoryPublisher _inventory;
    private readonly LogTailService _logTail;
    private readonly LogDumpHandler _logDump;
    private readonly ManagerCommandHandler _cmdHandler;
    private readonly ILogger<ManagerWorker> _logger;

    public ManagerWorker(INatsCommunicationService nats, ManagerSettings settings,
        ManagerStateStore store, ICameraEnumerator enumerator,
        AgentSupervisor supervisor, InventoryPublisher inventory,
        LogTailService logTail, LogDumpHandler logDump,
        ManagerCommandHandler cmdHandler, ILogger<ManagerWorker> logger)
    {
        _nats       = nats;
        _settings   = settings;
        _store      = store;
        _enumerator = enumerator;
        _supervisor = supervisor;
        _inventory  = inventory;
        _logTail    = logTail;
        _logDump    = logDump;
        _cmdHandler = cmdHandler;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _nats.ConnectAsync(_settings.NatsUrl);
        _logger.LogInformation("Manager started. PCId={PCId}", _settings.PCId);

        // 수신 명령 + 로그 덤프 요청 구독
        _cmdHandler.Subscribe();
        _logDump.Subscribe();

        // [S7] AgentUI의 카메라별 하트비트를 supervisor에 공급한다 — 생존 판정 + disable 재조정용.
        await _nats.SubscribeAgentStatusAsync(status => _supervisor.NoteHeartbeat(status.AgentId));

        // 최초 카메라 열거: 탐지 결과를 저장된 상태와 병합한다
        var discovered = _enumerator.Enumerate();
        foreach (var cam in discovered)
        {
            var existing = _store.GetByHardwareId(cam.HardwareId);
            if (existing is null)
            {
                _store.Upsert(new CameraEntry
                {
                    HardwareId  = cam.HardwareId,
                    OpenCvIndex = cam.OpenCvIndex,
                    FirstSeen   = DateTime.UtcNow,
                    LastSeen    = DateTime.UtcNow,
                    IsApproved  = false,
                });
                _logger.LogInformation("New camera discovered: {HwId} ({Name})", cam.HardwareId, cam.FriendlyName);
            }
            else
            {
                existing.LastSeen    = DateTime.UtcNow;
                existing.OpenCvIndex = cam.OpenCvIndex;
                _store.Upsert(existing);
            }
        }

        // 승인된 카메라의 런타임 로드 요청
        _supervisor.SpawnAll();

        // 실행 중인 모든 Agent의 로그 tail 시작
        foreach (var entry in _store.GetAll())
        {
            if (!string.IsNullOrEmpty(entry.AgentId))
            {
                var logDir = Path.Combine(_settings.InstallRoot, "logs", entry.AgentId);
                _logTail.Watch(entry.AgentId, logDir);
            }
        }

        // PnP 변경 감시 시작
        _enumerator.Changed += OnPnpChanged;
        _enumerator.StartWatching();

        // 초기 인벤토리 발행
        await _inventory.PublishAsync();

        // 주기적 재방송: core NATS는 발행 시점의 활성 구독자에게만 전달하므로, Master가 이 초기 방송
        // 이후 시작/재시작하면 인벤토리를 못 받아 장치 목록이 빈 채로 남는다. 현재 상태를 주기적으로
        // 다시 흘려 늦게 붙은 구독자도 다음 주기에 채워지게 한다. 변경 시 즉시 발행 경로는 그대로 유지.
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) { break; }
            await _inventory.PublishAsync();
        }
    }

    /// <summary>
    /// PnP 도착이면 신규 카메라를 등록하거나 LastSeen/OpenCvIndex를 갱신한다(승인된 카메라가
    /// 죽어 있으면 다시 로드 요청). 제거이면 런타임 언로드를 요청한다.
    /// 어느 쪽이든 인벤토리를 즉시 재발행한다.
    /// </summary>
    private void OnPnpChanged(PnpChange change)
    {
        var cam = change.Camera;
        if (change.ChangeType == PnpChangeType.Arrival)
        {
            var existing = _store.GetByHardwareId(cam.HardwareId);
            if (existing is null)
            {
                _store.Upsert(new CameraEntry
                {
                    HardwareId  = cam.HardwareId,
                    OpenCvIndex = cam.OpenCvIndex,
                    FirstSeen   = DateTime.UtcNow,
                    LastSeen    = DateTime.UtcNow,
                    IsApproved  = false,
                });
                _logger.LogInformation("PnP arrival: new camera {HwId}", cam.HardwareId);
            }
            else
            {
                existing.LastSeen    = DateTime.UtcNow;
                existing.OpenCvIndex = cam.OpenCvIndex;
                _store.Upsert(existing);
                if (existing.IsApproved && !_supervisor.IsRunning(cam.HardwareId))
                    _supervisor.Spawn(existing);
            }
        }
        else
        {
            _logger.LogInformation("PnP removal: {HwId}", cam.HardwareId);
            _supervisor.Kill(cam.HardwareId);
        }

        _ = _inventory.PublishAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // [S7] 서비스 중지 시 AgentUI 카메라를 언로드하지 않는다 — AgentUI는 로그온 예약 작업으로
        // 독립 실행되며 Manager가 내려가도 단독으로 계속 서비스해야 한다.
        _enumerator.StopWatching();
        _logTail.Dispose();
        await _nats.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
