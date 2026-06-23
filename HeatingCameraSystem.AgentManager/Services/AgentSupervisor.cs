using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    /// <summary>
    /// 카메라별 Agent.exe 프로세스 spawn / kill / respawn 관리.
    /// 지수 백오프: 1→2→5→15→60s, 5회 한계 후 영구 드롭.
    /// </summary>
    public class AgentSupervisor : IDisposable
    {
        private static readonly int[] BackoffSeconds = { 1, 2, 5, 15, 60 };
        private const int MaxRestartAttempts = 5;
        private const int StableRunSeconds = 600; // 10분 안정 실행 시 카운터 리셋

        private readonly ManagerSettings _settings;
        private readonly ManagerStateStore _store;
        private readonly ILogger<AgentSupervisor> _logger;
        private readonly ConcurrentDictionary<string, ManagedAgent> _agents = new();

        public event Action<string, string>? AgentDropped; // (hardwareId, reason)

        public AgentSupervisor(ManagerSettings settings, ManagerStateStore store,
            ILogger<AgentSupervisor> logger)
        {
            _settings = settings;
            _store = store;
            _logger = logger;
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
            Kill(entry.HardwareId);

            var logPath = Path.Combine(_settings.InstallRoot, "logs", entry.AgentId);
            Directory.CreateDirectory(logPath);

            var storagePath = string.IsNullOrEmpty(entry.StoragePath)
                ? Path.Combine(_settings.InstallRoot, "Agent", "ImageStorage", entry.AgentId)
                : entry.StoragePath;

            // [SC-12 범위 2] Design Ref: §4.3 — Agent CLI 인수 순서 (Agent/Program.cs 기준):
            //   [0]=AgentId  [1]=NatsUrl  [2]=CameraIndex  [3]=StoragePath  [4]=SimulationMode  [5]=LogPath
            // SimulationMode → SimulateAgentMode: true이면 Agent가 FakeCameraCaptureService를 사용.
            // Plan SC: SC-01 — 인수 순서가 맞아야 Agent가 FakeCam 모드로 기동되어 캡처 roundtrip 가능.
            var args = $"{entry.AgentId} {_settings.NatsUrl} {entry.OpenCvIndex} \"{storagePath}\" {_settings.SimulateAgentMode} \"{logPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _settings.AgentExePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            var managed = new ManagedAgent(entry.HardwareId, entry.AgentId, process);
            process.Exited += (_, _) => OnAgentExited(managed);

            // [SC-12 범위 2] Design Ref: §4.3 — spawn 스킵 조건에서 SimulationMode 제거.
            // 이전: SimulationMode=true이면 무조건 spawn 스킵 → 캡처 roundtrip 검증 불가.
            // 이후: exe 파일이 존재하면 실제 spawn 실행 (SimulateEnumeration=true여도 OK).
            //       SimulateAgentMode=true로 전달하면 Agent가 FakeCam으로 동작하므로
            //       실 카메라 없이도 전체 캡처 흐름을 테스트할 수 있음.
            if (!File.Exists(_settings.AgentExePath))
            {
                _logger.LogInformation("AgentExePath not found — skipping spawn for {AgentId}", entry.AgentId);
                _agents[entry.HardwareId] = managed;
                return;
            }

            process.Start();
            managed.StartedAt = DateTime.UtcNow;
            _agents[entry.HardwareId] = managed;
            _logger.LogInformation("Spawned {AgentId} PID={Pid}", entry.AgentId, process.Id);
        }

        public void Kill(string hardwareId)
        {
            if (!_agents.TryRemove(hardwareId, out var managed)) return;
            // SimulationMode 에서 spawn 을 건너뛴 Agent 는 시작되지 않은 Process 라
            // HasExited 접근 시 InvalidOperationException 이 발생한다 → 안전하게 no-op.
            try { if (managed.Process.HasExited) return; }
            catch (InvalidOperationException) { return; }

            try
            {
                managed.Process.CloseMainWindow();
                if (!managed.Process.WaitForExit(5000))
                    managed.Process.Kill();
                _logger.LogInformation("Killed {AgentId}", managed.AgentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kill failed for {AgentId}", managed.AgentId);
            }
        }

        public void KillAll()
        {
            foreach (var hardwareId in _agents.Keys)
                Kill(hardwareId);
        }

        public bool IsRunning(string hardwareId)
        {
            if (!_agents.TryGetValue(hardwareId, out var m)) return false;
            // SimulationMode 미시작 Process 는 HasExited 접근 시 예외 → 실행 안함으로 간주.
            try { return !m.Process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        public IReadOnlyCollection<string> RunningHardwareIds =>
            (IReadOnlyCollection<string>)_agents.Keys;

        private void OnAgentExited(ManagedAgent managed)
        {
            _agents.TryRemove(managed.HardwareId, out _);
            var entry = _store.GetByHardwareId(managed.HardwareId);
            if (entry is null || entry.IsDisabled) return;

            // 안정 실행 시 카운터 리셋
            if (managed.StartedAt != default &&
                (DateTime.UtcNow - managed.StartedAt).TotalSeconds >= StableRunSeconds)
            {
                entry.RestartFails = 0;
                _store.Upsert(entry);
            }

            entry.RestartFails++;
            _store.Upsert(entry);

            if (entry.RestartFails > MaxRestartAttempts)
            {
                _logger.LogError("{AgentId} crashed {Count} times — permanently dropped", managed.AgentId, MaxRestartAttempts);
                entry.IsDisabled = true;
                _store.Upsert(entry);
                AgentDropped?.Invoke(managed.HardwareId, $"Exceeded {MaxRestartAttempts} restart attempts");
                return;
            }

            int delay = BackoffSeconds[Math.Min(entry.RestartFails - 1, BackoffSeconds.Length - 1)];
            _logger.LogWarning("{AgentId} exited, restart in {Delay}s (attempt {N})",
                managed.AgentId, delay, entry.RestartFails);

            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delay));
                var refreshedEntry = _store.GetByHardwareId(managed.HardwareId);
                if (refreshedEntry is { IsApproved: true, IsDisabled: false })
                    Spawn(refreshedEntry);
            });
        }

        public void Dispose()
        {
            foreach (var m in _agents.Values)
            {
                try { if (!m.Process.HasExited) m.Process.Kill(); }
                catch { /* best effort */ }
                m.Process.Dispose();
            }
            _agents.Clear();
        }
    }

    internal class ManagedAgent
    {
        public string  HardwareId { get; }
        public string  AgentId    { get; }
        public Process Process    { get; }
        public DateTime StartedAt { get; set; }

        public ManagedAgent(string hardwareId, string agentId, Process process)
        {
            HardwareId = hardwareId;
            AgentId    = agentId;
            Process    = process;
        }
    }
}
