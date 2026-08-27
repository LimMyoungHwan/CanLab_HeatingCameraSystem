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
    /// [S7] 재정의됨. Manager는 더 이상 카메라마다 Agent.exe를 spawn하지 않는다. 로그온 예약 작업(S8)이
    /// 실행하는 단일 WPF AgentUI 프로세스(이 세션 0 서비스가 실행하는 일은 없다)가 로컬 카메라 전부를
    /// 소유한다. 이 supervisor는 카메라별로 "원하는" 로드 상태를 들고 있다가 NATS
    /// (<c>master.cmd.camera.{AgentId}</c>의 runtimeLoad / runtimeUnload)로 그 프로세스에 밀어 넣는다.
    /// 프로세스를 죽이는 일이 없으므로 카메라 하나를 거부·비활성화해도 나머지가 함께 떨어질 수 없다.
    /// 카메라를 "실행 중"으로 치는 조건은 원하는 상태가 로드이면서 해당 AgentId의 신선한 AgentUI
    /// 하트비트가 들어오고 있을 때뿐이다.
    ///
    /// 기존 AgentManagerTests가 계속 컴파일되도록 공개 메서드 시그니처는 유지한다.
    /// </summary>
    public class AgentSupervisor : IDisposable
    {
        /// <summary>하트비트가 이보다 오래되면 해당 AgentId를 죽은 것으로 본다.</summary>
        private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(15);

        private readonly ManagerSettings _settings;
        private readonly ManagerStateStore _store;
        private readonly ILogger<AgentSupervisor> _logger;
        private readonly INatsCommunicationService? _nats;

        private readonly ConcurrentDictionary<string, bool> _loaded = new();
        private readonly ConcurrentDictionary<string, DateTime> _heartbeatUtc = new();

        /// <summary>NATS 없이 생성하는 테스트용 편의 생성자. 런타임 명령 발행은 조용히 무시된다.</summary>
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

        /// <summary>승인되었고 비활성화되지 않은 카메라 전부에 대해 런타임 로드를 요청한다.</summary>
        public void SpawnAll()
        {
            foreach (var entry in _store.GetAll())
            {
                if (!entry.IsApproved || entry.IsDisabled) continue;
                Spawn(entry);
            }
        }

        /// <summary>카메라를 원하는-로드 상태로 표시하고 AgentUI에 runtimeLoad를 발행한다. 이름과 달리 프로세스를 만들지 않는다.</summary>
        public void Spawn(CameraEntry entry)
        {
            _loaded[entry.HardwareId] = true;
            PublishRuntime(entry.AgentId, CameraControlOps.RuntimeLoad, entry.OpenCvIndex);
            _logger.LogInformation("Runtime load requested: {AgentId} (hw={HwId})", entry.AgentId, entry.HardwareId);
        }

        /// <summary>원하는-로드 상태를 해제하고 AgentUI에 runtimeUnload를 발행한다. 프로세스를 죽이지 않는다.</summary>
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

        /// <summary>원하는 상태가 로드이고 해당 AgentId의 하트비트가 <see cref="HeartbeatTtl"/> 이내로 신선할 때만 true.</summary>
        public bool IsRunning(string hardwareId)
        {
            if (!_loaded.TryGetValue(hardwareId, out bool loaded) || !loaded) return false;
            var entry = _store.GetByHardwareId(hardwareId);
            return entry is not null && HeartbeatFresh(entry.AgentId);
        }

        public IReadOnlyCollection<string> RunningHardwareIds =>
            _loaded.Keys.Where(IsRunning).ToList();

        /// <summary>
        /// [S7] Manager의 <c>agent.status.*</c> 구독이 공급한다. 해당 AgentId의 생존 시각을 갱신하고,
        /// Option-A 재조정에 따라 운영자가 비활성화·거부한 카메라를 AgentUI가 (재)열었으면 언로드를
        /// 다시 발행한다 — AgentUI는 (재)시작 시 자기 설정의 카메라를 전부 열기 때문에, 그대로 두면
        /// 하트비트 1회 정도의 짧은 창 안에서 비활성화된 카메라가 다시 열릴 수 있다.
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

        /// <summary><c>master.cmd.camera.{AgentId}</c>로 런타임 로드/언로드 명령을 발행한다. NATS 미주입(테스트)이면 무시한다.</summary>
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
