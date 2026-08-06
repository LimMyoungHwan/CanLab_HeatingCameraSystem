using System;
using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    // Live alias -> current AgentId map, fed by agent.status heartbeats, so recipes route by the
    // operator's stable alias instead of the volatile host_Agent_n slot the camera runs under.
    // Last-write-wins by alias: a re-slotted camera (same alias, new AgentId) self-corrects on its
    // next heartbeat. No eviction — a stale entry only routes to an offline agent, which fails the
    // capture the same as an unknown alias would.
    public sealed class AgentDirectory
    {
        private readonly ConcurrentDictionary<string, string> _byAlias = new(StringComparer.OrdinalIgnoreCase);

        public void Note(AgentStatusMessage message)
        {
            if (message is null ||
                string.IsNullOrWhiteSpace(message.Alias) ||
                string.IsNullOrWhiteSpace(message.AgentId))
            {
                return;
            }

            _byAlias[message.Alias] = message.AgentId;
        }

        public string? ResolveByAlias(string? alias) =>
            !string.IsNullOrWhiteSpace(alias) && _byAlias.TryGetValue(alias, out string? agentId)
                ? agentId
                : null;
    }
}
