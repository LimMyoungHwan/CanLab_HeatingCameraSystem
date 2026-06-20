using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    public class ManagerCommandHandler
    {
        private readonly INatsCommunicationService _nats;
        private readonly ManagerSettings _settings;
        private readonly ManagerStateStore _store;
        private readonly AgentSupervisor _supervisor;
        private readonly InventoryPublisher _inventory;
        private readonly ILogger<ManagerCommandHandler> _logger;

        public ManagerCommandHandler(INatsCommunicationService nats, ManagerSettings settings,
            ManagerStateStore store, AgentSupervisor supervisor,
            InventoryPublisher inventory, ILogger<ManagerCommandHandler> logger)
        {
            _nats      = nats;
            _settings  = settings;
            _store     = store;
            _supervisor = supervisor;
            _inventory = inventory;
            _logger    = logger;
        }

        public void Subscribe()
        {
            _nats.SubscribeManagerCommandAsync(_settings.PCId, cmd => _ = HandleAsync(cmd));
        }

        private async Task HandleAsync(ManagerCommandMessage cmd)
        {
            _logger.LogInformation("ManagerCommand: {Op} for {HwId}", cmd.Op, cmd.HardwareId);

            var entry = _store.GetByHardwareId(cmd.HardwareId);
            if (entry is null && cmd.Op != ManagerCommandOp.Approve)
            {
                _logger.LogWarning("ManagerCommand: unknown HardwareId {HwId}", cmd.HardwareId);
                return;
            }

            switch (cmd.Op)
            {
                case ManagerCommandOp.Approve:
                    await ApproveAsync(cmd);
                    break;

                case ManagerCommandOp.Reject:
                    entry!.IsApproved = false;
                    _supervisor.Kill(cmd.HardwareId);
                    _store.Upsert(entry);
                    break;

                case ManagerCommandOp.Rename:
                    entry!.Alias = cmd.Payload;
                    _store.Upsert(entry);
                    break;

                case ManagerCommandOp.SetSerial:
                    // Payload = JSON serialized CameraSerialSettings — forward to Agent via NATS
                    // (Agent already handles master.config.serial.{AgentId} topic)
                    break;

                case ManagerCommandOp.Restart:
                    if (entry is not null)
                    {
                        _supervisor.Kill(cmd.HardwareId);
                        _supervisor.Spawn(entry);
                    }
                    break;

                case ManagerCommandOp.Disable:
                    entry!.IsDisabled = true;
                    _supervisor.Kill(cmd.HardwareId);
                    _store.Upsert(entry);
                    break;
            }

            await _inventory.PublishAsync();
        }

        private async Task ApproveAsync(ManagerCommandMessage cmd)
        {
            var entry = _store.GetByHardwareId(cmd.HardwareId);
            if (entry is null)
            {
                _logger.LogWarning("Approve: HardwareId {HwId} not in state", cmd.HardwareId);
                return;
            }

            entry.IsApproved = true;
            entry.IsDisabled = false;

            if (!string.IsNullOrEmpty(cmd.Payload))
                entry.Alias = cmd.Payload;

            if (string.IsNullOrEmpty(entry.AgentId))
                entry.AgentId = BuildAgentId(_settings.PCId, cmd.HardwareId);

            _store.Upsert(entry);
            _supervisor.Spawn(entry);
            await _inventory.PublishAsync();
        }

        public static string BuildAgentId(string pcId, string hardwareId)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(hardwareId));
            string hash8 = Convert.ToHexString(hash)[..8].ToLower();
            return $"{pcId}_{hash8}";
        }
    }
}
