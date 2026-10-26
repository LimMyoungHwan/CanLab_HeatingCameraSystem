using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// agent.status 하트비트로 채워지는 alias → 현재 AgentId 실시간 매핑.
    /// 레시피가 카메라가 실제로 돌아가는 가변 host_Agent_n 슬롯 대신 운영자의 안정적인 alias로
    /// 라우팅할 수 있게 한다.
    /// 호스트 인벤토리·영상 상태를 함께 확인해 사라진 카메라와 보고 전용 Agent는 제외한다.
    /// </summary>
    public sealed class AgentDirectory
    {
        /// <summary>하트비트가 이 시간 안에 들어왔으면 온라인으로 본다. 하트비트 주기(5초)의 3배다.</summary>
        public static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(15);

        private readonly ConcurrentDictionary<string, string> _byAlias = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _cameraTemperatures = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _agentHosts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, (CameraIdentity Camera, DateTime Seen)> _cameras = new(StringComparer.Ordinal);

        public IReadOnlyList<CameraIdentity> OnlineCameras => _cameras.Values
            .Where(item => DateTime.UtcNow - item.Seen <= OnlineThreshold)
            .OrderBy(item => item.Camera.AgentId, StringComparer.Ordinal).ThenBy(item => item.Camera.CameraIndex)
            .Select(item => item.Camera.Copy()).ToArray();

        public bool IsCameraOnline(CameraIdentity camera) =>
            _cameras.TryGetValue(camera.Key, out var item) && DateTime.UtcNow - item.Seen <= OnlineThreshold;

        /// <summary>하트비트 한 건에서 alias → AgentId 매핑과 카메라 온도를 갱신한다.</summary>
        public void Note(AgentStatusMessage message)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.AgentId)) return;

            if (message.HostAgentIds is { } inventory)
            {
                var live = new HashSet<string>(inventory, StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(message.HostName))
                    foreach (var entry in _agentHosts)
                        if (string.Equals(entry.Value, message.HostName, StringComparison.OrdinalIgnoreCase)
                            && !live.Contains(entry.Key))
                            RemoveAgent(entry.Key);

                // 카메라 0대의 보고용 ID를 온라인 카메라로 등록하지 않는다.
                if (!live.Contains(message.AgentId))
                {
                    RemoveAgent(message.AgentId);
                    return;
                }
            }
            if (message.CameraStatus == CameraStatus.Offline)
            {
                RemoveAgent(message.AgentId);
                return;
            }

            var now = DateTime.UtcNow;
            _lastSeen[message.AgentId] = now;
            if (!string.IsNullOrWhiteSpace(message.HostName)) _agentHosts[message.AgentId] = message.HostName;
            var camera = new CameraIdentity { AgentId = message.AgentId, CameraIndex = message.CameraIndex };
            // 인벤토리를 보고하는 AgentUI는 AgentId당 카메라 한 대다. 구형 다중 카메라 Agent는 유지한다.
            if (message.HostAgentIds is not null)
                foreach (var entry in _cameras)
                    if (entry.Value.Camera.AgentId == message.AgentId && entry.Key != camera.Key)
                        _cameras.TryRemove(entry.Key, out _);
            _cameras[camera.Key] = (camera, now);

            // 온도는 alias가 없어도 AgentId만 있으면 기록한다. null이면 직전 값을 지우지 않는다.
            if (message.CameraTemperature.HasValue)
                _cameraTemperatures[message.AgentId] = message.CameraTemperature.Value;

            if (string.IsNullOrWhiteSpace(message.Alias)) return;
            _byAlias[message.Alias] = message.AgentId;
        }

        private void RemoveAgent(string agentId)
        {
            _lastSeen.TryRemove(agentId, out _);
            _cameraTemperatures.TryRemove(agentId, out _);
            _agentHosts.TryRemove(agentId, out _);
            foreach (var entry in _cameras)
                if (entry.Value.Camera.AgentId == agentId) _cameras.TryRemove(entry.Key, out _);
            foreach (var entry in _byAlias)
                if (entry.Value == agentId)
                    ((ICollection<KeyValuePair<string, string>>)_byAlias).Remove(entry);
        }

        /// <summary>하트비트로 들어온 AgentId별 최신 카메라 온도 스냅샷.</summary>
        public IReadOnlyDictionary<string, double> CameraTemperatures =>
            new Dictionary<string, double>(_cameraTemperatures, StringComparer.Ordinal);

        /// <summary>
        /// 하트비트가 <see cref="OnlineThreshold"/> 안에 들어온 Agent인지 본다.
        /// 한 번도 못 본 AgentId도 false다 — 레시피는 이 둘을 구분할 필요가 없다(둘 다 명령이 못 간다).
        /// </summary>
        public bool IsAgentOnline(string? agentId) =>
            !string.IsNullOrWhiteSpace(agentId)
            && _lastSeen.TryGetValue(agentId, out DateTime seen)
            && DateTime.UtcNow - seen <= OnlineThreshold;

        /// <summary>alias로 현재 AgentId를 찾는다. 모르는 alias면 null을 돌려준다.</summary>
        public string? ResolveByAlias(string? alias) =>
            !string.IsNullOrWhiteSpace(alias) && _byAlias.TryGetValue(alias, out string? agentId)
                ? agentId
                : null;
    }
}
