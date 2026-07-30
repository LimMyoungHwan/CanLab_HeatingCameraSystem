using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    /// <summary>
    /// [S7] Redefined. The Manager no longer spawns one Agent.exe per camera. A single WPF
    /// AgentUI process (launched by a logon Scheduled Task — S8, never by this Session-0 service)
    /// owns every local camera. This supervisor holds the DESIRED per-camera load state and drives
    /// it into that process over NATS (runtimeLoad / runtimeUnload); it never kills a process, so
    /// rejecting or disabling one camera can never drop the others. A camera counts as "running"
    /// only when it is desired-loaded AND a fresh AgentUI heartbeat is arriving for its AgentId.
    ///
    /// Public method signatures are preserved so the existing AgentManagerTests keep compiling.
    /// </summary>
    public class AgentSupervisor : IDisposable
    {
        private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(15);

        private readonly ManagerSettings _settings;
        private readonly ManagerStateStore _store;
        private readonly ILogger<AgentSupervisor> _logger;
        private readonly INatsCommunicationService? _nats;

        private readonly ConcurrentDictionary<string, bool> _loaded = new();
        private readonly ConcurrentDictionary<string, DateTime> _heartbeatUtc = new();

        public AgentSupervisor(ManagerSettings settings, ManagerStateStore store,
            ILogger<AgentSupervisor> logger)
            : this(settings, store, logger, null)
        {
        }

        public AgentSupervisor(ManagerSettings settings, ManagerStateStore store,
            ILogger<AgentSupervisor> logger, INatsCommunicationService? nats)
        {
            _settings = settings;
            _store = store;
            _logger = logger;
            _nats = nats;
        }

        public void SpawnAll()
        {
            foreach (var entry in _store.GetAll())
            {
                if (!entry.IsApproved || entry.IsDisabled) continue;
                Spawn(entry);
            }
        }

        public void Spawn(CameraEntry entry)
        {
            _loaded[entry.HardwareId] = true;
            PublishRuntime(entry.AgentId, CameraControlOps.RuntimeLoad, entry.OpenCvIndex);
            _logger.LogInformation("Runtime load requested: {AgentId} (hw={HwId})", entry.AgentId, entry.HardwareId);
        }

        public void Kill(string hardwareId)
        {
            _loaded[hardwareId] = false;
            var entry = _store.GetByHardwareId(hardwareId);
            if (entry is not null)
            {
                PublishRuntime(entry.AgentId, CameraControlOps.RuntimeUnload, entry.OpenCvIndex);
                _logger.LogInformation("Runtime unload requested: {AgentId} (hw={HwId})", entry.AgentId, hardwareId);
            }
        }

        public void KillAll()
        {
            foreach (var hardwareId in _loaded.Keys.ToList())
                Kill(hardwareId);
        }

        public bool IsRunning(string hardwareId)
        {
            if (!_loaded.TryGetValue(hardwareId, out bool loaded) || !loaded) return false;
            var entry = _store.GetByHardwareId(hardwareId);
            return entry is not null && HeartbeatFresh(entry.AgentId);
        }

        public IReadOnlyCollection<string> RunningHardwareIds =>
            _loaded.Keys.Where(IsRunning).ToList();

        /// <summary>
        /// [S7] Fed by the Manager's <c>agent.status.*</c> subscription. Refreshes the AgentId's
        /// liveness and, per the Option-A reconcile, re-issues an unload when AgentUI has (re)opened
        /// a camera the operator disabled or rejected — AgentUI opens every camera in its own config
        /// on (re)start, so a bounded ~1-heartbeat window can otherwise reopen a disabled camera.
        /// </summary>
        public void NoteHeartbeat(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            _heartbeatUtc[agentId] = DateTime.UtcNow;

            var entry = _store.GetByAgentId(agentId);
            if (entry is not null && (entry.IsDisabled || !entry.IsApproved))
                PublishRuntime(agentId, CameraControlOps.RuntimeUnload, entry.OpenCvIndex);
        }

        private bool HeartbeatFresh(string agentId) =>
            _heartbeatUtc.TryGetValue(agentId, out var last) && DateTime.UtcNow - last < HeartbeatTtl;

        private void PublishRuntime(string agentId, string op, int cameraIndex)
        {
            if (_nats is null || string.IsNullOrEmpty(agentId)) return;
            _ = _nats.PublishCameraControlAsync(new CameraControlMessage
            {
                AgentId = agentId,
                CameraIndex = cameraIndex,
                Op = op,
                Timestamp = DateTime.UtcNow,
            });
        }

        public void Dispose()
        {
            _loaded.Clear();
            _heartbeatUtc.Clear();
        }
    }
}
