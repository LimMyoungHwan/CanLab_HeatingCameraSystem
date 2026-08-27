using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentManager.State
{
    /// <summary>
    /// manager-state.json 의 영속 저장 + 인메모리 캐시.
    /// 스레드 안전 (lock).
    /// </summary>
    public class ManagerStateStore
    {
        private readonly string _statePath;
        private readonly object _lock = new();
        private ManagerState _state = new();

        public ManagerStateStore(string installRoot)
        {
            _statePath = Path.Combine(installRoot, "Manager", "manager-state.json");
        }

        /// <summary>manager-state.json을 읽어 캐시를 채운다. 파일이 없으면 빈 상태로 시작한다.</summary>
        public void Load()
        {
            if (!File.Exists(_statePath)) return;
            lock (_lock)
            {
                var json = File.ReadAllText(_statePath);
                _state = JsonSerializer.Deserialize<ManagerState>(json) ?? new ManagerState();
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                File.WriteAllText(_statePath,
                    JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public IReadOnlyList<CameraEntry> GetAll()
        {
            lock (_lock)
                return _state.Cameras.ToList();
        }

        public CameraEntry? GetByHardwareId(string hardwareId)
        {
            lock (_lock)
                return _state.Cameras.FirstOrDefault(c => c.HardwareId == hardwareId);
        }

        // [S7] 논리 AgentId 역방향 조회 — AgentSupervisor.NoteHeartbeat가 (AgentId 기준) 하트비트를
        // (HardwareId 기준) CameraEntry로 되짚을 때 사용한다.
        public CameraEntry? GetByAgentId(string agentId)
        {
            lock (_lock)
                return _state.Cameras.FirstOrDefault(c => c.AgentId == agentId);
        }

        /// <summary>HardwareId 기준으로 교체 삽입하고 즉시 파일로 저장한다.</summary>
        public void Upsert(CameraEntry entry)
        {
            lock (_lock)
            {
                var existing = _state.Cameras.FirstOrDefault(c => c.HardwareId == entry.HardwareId);
                if (existing is not null) _state.Cameras.Remove(existing);
                _state.Cameras.Add(entry);
            }
            Save();
        }

        public void Remove(string hardwareId)
        {
            lock (_lock)
                _state.Cameras.RemoveAll(c => c.HardwareId == hardwareId);
            Save();
        }
    }

    /// <summary>manager-state.json에 직렬화되는 루트 객체.</summary>
    public class ManagerState
    {
        public string PCId { get; set; } = Environment.MachineName;
        public List<CameraEntry> Cameras { get; set; } = new();
    }

    /// <summary>
    /// Manager가 기억하는 카메라 한 대의 영속 상태. HardwareId가 안정 식별자이고
    /// OpenCvIndex는 재연결로 바뀔 수 있다.
    /// </summary>
    public class CameraEntry
    {
        public string   HardwareId    { get; set; } = string.Empty;
        public string   AgentId       { get; set; } = string.Empty;
        public string   Alias         { get; set; } = string.Empty;
        public int      OpenCvIndex   { get; set; }
        public string   StoragePath   { get; set; } = string.Empty;
        public bool     IsApproved    { get; set; }
        public DateTime FirstSeen     { get; set; }
        public DateTime LastSeen      { get; set; }
        public int      RestartFails  { get; set; }
        public bool     IsDisabled    { get; set; }
    }
}
