using System;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    public class InventoryPublisher
    {
        private readonly INatsCommunicationService _nats;
        private readonly ManagerSettings _settings;
        private readonly ManagerStateStore _store;
        private readonly AgentSupervisor _supervisor;
        private readonly ILogger<InventoryPublisher> _logger;

        public InventoryPublisher(INatsCommunicationService nats, ManagerSettings settings,
            ManagerStateStore store, AgentSupervisor supervisor,
            ILogger<InventoryPublisher> logger)
        {
            _nats       = nats;
            _settings   = settings;
            _store      = store;
            _supervisor = supervisor;
            _logger     = logger;
        }

        public async Task PublishAsync()
        {
            var cameras = _store.GetAll()
                .Select(e => new CameraInventoryItem
                {
                    HardwareId  = e.HardwareId,
                    Alias       = e.Alias,
                    AgentId     = e.AgentId,
                    OpenCvIndex = e.OpenCvIndex,
                    IsApproved  = e.IsApproved,
                    IsRunning   = _supervisor.IsRunning(e.HardwareId),
                    LastSeen    = e.LastSeen,
                })
                .ToList();

            var message = new CameraInventoryMessage
            {
                PCId      = _settings.PCId,
                Cameras   = cameras,
                Timestamp = DateTime.UtcNow,
            };

            await _nats.PublishCameraInventoryAsync(message);
            _logger.LogDebug("Published inventory: {Count} cameras", cameras.Count);
        }
    }
}
