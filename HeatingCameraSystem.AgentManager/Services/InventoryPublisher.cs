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
    /// <summary>
    /// 상태 저장소의 카메라 목록을 <see cref="CameraInventoryMessage"/>로 만들어
    /// <c>agent-mgr.inventory.{PCId}</c>로 발행한다. IsRunning은 supervisor의 생존 판정을 따른다.
    /// </summary>
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

        /// <summary>현재 카메라 인벤토리 전체를 한 번 발행한다.</summary>
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
