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
    /// alias 기준 last-write-wins: 슬롯이 바뀐 카메라(같은 alias, 새 AgentId)는 다음 하트비트에서
    /// 스스로 교정된다. 항목 제거(eviction)는 없다 — 오래된 항목은 오프라인 Agent로 라우팅될 뿐이며,
    /// 이는 모르는 alias와 똑같이 캡처 실패로 끝난다.
    /// </summary>
    public sealed class AgentDirectory
    {
        private readonly ConcurrentDictionary<string, string> _byAlias = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, double> _cameraTemperatures = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>하트비트 한 건에서 alias → AgentId 매핑과 카메라 온도를 갱신한다.</summary>
        public void Note(AgentStatusMessage message)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.AgentId)) return;

            // 온도는 alias가 없어도 AgentId만 있으면 기록한다. null이면 직전 값을 지우지 않는다.
            if (message.CameraTemperature.HasValue)
                _cameraTemperatures[message.AgentId] = message.CameraTemperature.Value;

            if (string.IsNullOrWhiteSpace(message.Alias)) return;
            _byAlias[message.Alias] = message.AgentId;
        }

        /// <summary>하트비트로 들어온 AgentId별 최신 카메라 온도 스냅샷.</summary>
        public IReadOnlyDictionary<string, double> CameraTemperatures =>
            new Dictionary<string, double>(_cameraTemperatures, StringComparer.OrdinalIgnoreCase);

        /// <summary>alias로 현재 AgentId를 찾는다. 모르는 alias면 null을 돌려준다.</summary>
        public string? ResolveByAlias(string? alias) =>
            !string.IsNullOrWhiteSpace(alias) && _byAlias.TryGetValue(alias, out string? agentId)
                ? agentId
                : null;
    }
}
