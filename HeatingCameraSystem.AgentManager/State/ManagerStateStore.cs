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

    public class ManagerState
    {
        public string PCId { get; set; } = Environment.MachineName;
        public List<CameraEntry> Cameras { get; set; } = new();
    }

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
