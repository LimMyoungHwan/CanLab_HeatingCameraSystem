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

// ── Settings ─────────────────────────────────────────────────────────────────
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

// ── Camera Enumerator ─────────────────────────────────────────────────────────
// [SC-12 범위 2] Design Ref: §4.2 — SimulationMode → SimulateEnumeration.
// SimulateEnumeration=true 이면 실 카메라 없이 가상 카메라 2대를 반환하는 FakeCameraEnumerator 사용.
// false 이면 WMI로 실제 연결된 USB 카메라를 탐지하는 WmiCameraEnumerator 사용.
builder.Services.AddSingleton<ICameraEnumerator>(sp =>
    settings.SimulateEnumeration
        ? (ICameraEnumerator)new FakeCameraEnumerator()
        : new WmiCameraEnumerator());

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AgentSupervisor>();
builder.Services.AddSingleton<InventoryPublisher>();
builder.Services.AddSingleton<LogTailService>();
builder.Services.AddSingleton<LogDumpHandler>();
builder.Services.AddSingleton<ManagerCommandHandler>();
builder.Services.AddHostedService<ManagerWorker>();

var host = builder.Build();
await host.RunAsync();

// ── Worker ────────────────────────────────────────────────────────────────────

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

        // Subscribe for inbound commands + log dump requests
        _cmdHandler.Subscribe();
        _logDump.Subscribe();

        // [S7] Feed AgentUI per-camera heartbeats into the supervisor for liveness + disable reconcile.
        await _nats.SubscribeAgentStatusAsync(status => _supervisor.NoteHeartbeat(status.AgentId));

        // Initial camera enumeration: merge discovered with stored state
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

        // Spawn approved cameras
        _supervisor.SpawnAll();

        // Start log tailing for all running agents
        foreach (var entry in _store.GetAll())
        {
            if (!string.IsNullOrEmpty(entry.AgentId))
            {
                var logDir = Path.Combine(_settings.InstallRoot, "logs", entry.AgentId);
                _logTail.Watch(entry.AgentId, logDir);
            }
        }

        // PnP change watcher
        _enumerator.Changed += OnPnpChanged;
        _enumerator.StartWatching();

        // Publish initial inventory
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
        // [S7] Do NOT unload AgentUI cameras on service stop — AgentUI runs independently
        // (logon Scheduled Task) and must keep serving standalone when the Manager is down.
        _enumerator.StopWatching();
        _logTail.Dispose();
        await _nats.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
