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
builder.Services.AddWindowsService(opts => opts.ServiceName = "HCS-Manager");

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
builder.Services.AddSingleton<ICameraEnumerator>(sp =>
    settings.SimulationMode
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

        // Alert on permanently dropped agents
        _supervisor.AgentDropped += (hwId, reason) =>
        {
            _ = _nats.PublishLogAlertAsync(new LogAlertMessage
            {
                PCId      = _settings.PCId,
                AgentId   = _store.GetByHardwareId(hwId)?.AgentId ?? hwId,
                Level     = LogAlertLevel.Fatal,
                Message   = $"Agent permanently dropped: {reason}",
                Timestamp = DateTime.UtcNow,
            });
        };

        // Publish initial inventory
        await _inventory.PublishAsync();

        await Task.Delay(Timeout.Infinite, stoppingToken);
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
        _enumerator.StopWatching();
        _supervisor.KillAll();
        _logTail.Dispose();
        await _nats.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
