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
    /// <summary>
    /// <c>server.cmd.mgr.{PCId}</c>로 들어오는 서버 명령(Approve/Reject/Rename/SetSerial/Restart/Disable)을
    /// 처리한다. 대상 카메라는 안정 식별자인 HardwareId로 지정되며, 처리 후 매번 인벤토리를 재발행한다.
    /// </summary>
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

        /// <summary><c>server.cmd.mgr.{PCId}</c> 구독을 시작한다.</summary>
        public void Subscribe()
        {
            _nats.SubscribeManagerCommandAsync(_settings.PCId, cmd => _ = HandleAsync(cmd));
        }

        /// <summary>
        /// 명령 한 건을 Op별로 분기 처리한다. Approve를 제외하면 미지의 HardwareId는 경고만 남기고 버린다.
        /// SetSerial은 Payload의 시리얼 설정을 <c>master.config.serial.{AgentId}</c>로 전달한다.
        /// </summary>
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
                    if (entry is not null && !string.IsNullOrEmpty(entry.AgentId) &&
                        !string.IsNullOrEmpty(cmd.Payload))
                    {
                        try
                        {
                            var serialSettings = JsonSerializer.Deserialize<Core.Models.CameraSerialSettings>(cmd.Payload);
                            if (serialSettings is not null)
                            {
                                await _nats.PublishSerialConfigAsync(new Core.Models.SerialConfigMessage
                                {
                                    AgentId   = entry.AgentId,
                                    Settings  = serialSettings,
                                    Timestamp = DateTime.UtcNow,
                                });
                                _logger.LogInformation("SetSerial forwarded to {AgentId}", entry.AgentId);
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning(ex, "SetSerial: invalid Payload JSON for {HwId}", cmd.HardwareId);
                        }
                    }
                    break;

                case ManagerCommandOp.Restart:
                    // [S7] runtimeLoad는 멱등한 재로드이므로 재시작은 AgentUI로 보내는 Load 메시지
                    // 한 건이면 된다 — unload->load NATS 경합이 없다.
                    if (entry is not null)
                        _supervisor.Spawn(entry);
                    break;

                case ManagerCommandOp.Disable:
                    entry!.IsDisabled = true;
                    _supervisor.Kill(cmd.HardwareId);
                    _store.Upsert(entry);
                    break;
            }

            await _inventory.PublishAsync();
        }

        /// <summary>
        /// 승인 처리: 승인·활성화 표시하고, Payload가 있으면 Alias로 쓰며, AgentId가 없으면
        /// <see cref="BuildAgentId"/>로 만들어 붙인 뒤 런타임 로드를 요청한다.
        /// </summary>
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

        /// <summary>HardwareId의 SHA-256 앞 8자리(hex 소문자)를 붙여 <c>{PCId}_{hash8}</c> 형태의 안정적인 AgentId를 만든다.</summary>
        public static string BuildAgentId(string pcId, string hardwareId)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(hardwareId));
            string hash8 = Convert.ToHexString(hash)[..8].ToLower();
            return $"{pcId}_{hash8}";
        }
    }
}
