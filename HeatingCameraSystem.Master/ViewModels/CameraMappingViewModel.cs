using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class MappingCamera : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotAssigned))]
        private bool _isAssigned;

        public bool IsNotAssigned => !IsAssigned;
    }

    public partial class MappingAgent : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private int _totalCount;

        public ObservableCollection<MappingCamera> Cameras { get; } = new ObservableCollection<MappingCamera>();
    }

    public partial class MappingSlot : ObservableObject
    {
        [ObservableProperty]
        private string _positionId = string.Empty; // "P01" ~ "P64"

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCamera))]
        private string? _cameraId;

        public bool HasCamera => !string.IsNullOrEmpty(CameraId);
    }

    public partial class CameraMappingViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AssignedRatioText))]
        private int _assignedCount;

        [ObservableProperty]
        private int _totalSlots = 64;

        public string AssignedRatioText => $"{AssignedCount} / {TotalSlots}";

        [ObservableProperty]
        private string _syncStatusText = "Ready";

        [ObservableProperty]
        private double _syncProgress = 75;

        public ObservableCollection<MappingAgent> AvailableAgents { get; } = new ObservableCollection<MappingAgent>();
        public ObservableCollection<MappingSlot> Slots { get; } = new ObservableCollection<MappingSlot>();

        public CameraMappingViewModel()
        {
            InitializeData();
        }

        private void InitializeData()
        {
            // 1. 64 Grid Slots
            for (int i = 1; i <= 64; i++)
            {
                Slots.Add(new MappingSlot
                {
                    PositionId = $"P{i:D2}",
                    CameraId = null
                });
            }

            // 2. Available Cameras & Agents
            // 4 Agents, each with 16 cameras
            for (int i = 1; i <= 4; i++)
            {
                var agent = new MappingAgent
                {
                    Name = $"Agent PC #{i}",
                    TotalCount = 16,
                    IsExpanded = i == 1
                };

                for (int j = 1; j <= 16; j++)
                {
                    agent.Cameras.Add(new MappingCamera
                    {
                        Id = $"CAM-{(i - 1) * 16 + j:D2}",
                        IsAssigned = false
                    });
                }

                AvailableAgents.Add(agent);
            }

            var saved = AppServices.MappingRepo.GetAllAsync().GetAwaiter().GetResult().ToList();
            foreach (var m in saved.Where(m => m.CameraId != null))
            {
                var slot = Slots.FirstOrDefault(s => s.PositionId == m.SlotId);
                if (slot == null) continue;
                slot.CameraId = m.CameraId;
                var cam = AvailableAgents.SelectMany(a => a.Cameras).FirstOrDefault(c => c.Id == m.CameraId);
                if (cam != null) cam.IsAssigned = true;
            }

            UpdateAssignedCount();
        }

        private void UpdateAssignedCount()
        {
            AssignedCount = Slots.Count(s => s.HasCamera);
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var slot in Slots)
            {
                slot.CameraId = null;
            }

            foreach (var agent in AvailableAgents)
            {
                foreach (var cam in agent.Cameras)
                {
                    cam.IsAssigned = false;
                }
            }

            UpdateAssignedCount();
        }

        [RelayCommand]
        private void SaveMapping()
        {
            var mappings = Slots.Select(s => new CameraMappingConfig { SlotId = s.PositionId, CameraId = s.CameraId });
            AppServices.MappingRepo.SaveAllAsync(mappings).GetAwaiter().GetResult();
        }

        [RelayCommand]
        private void AssignCameraToSlot(Tuple<MappingCamera, MappingSlot> param)
        {
            if (param == null) return;
            var camera = param.Item1;
            var slot = param.Item2;

            // If the camera is already assigned elsewhere, unassign it first
            var existingSlot = Slots.FirstOrDefault(s => s.CameraId == camera.Id);
            if (existingSlot != null)
            {
                existingSlot.CameraId = null;
            }

            // If slot already had a camera, mark that camera as unassigned
            if (slot.HasCamera)
            {
                var oldCamId = slot.CameraId;
                var oldCam = AvailableAgents.SelectMany(a => a.Cameras).FirstOrDefault(c => c.Id == oldCamId);
                if (oldCam != null)
                {
                    oldCam.IsAssigned = false;
                }
            }

            // Assign new camera
            slot.CameraId = camera.Id;
            camera.IsAssigned = true;
            UpdateAssignedCount();
        }

        [RelayCommand]
        private void UnassignSlot(MappingSlot slot)
        {
            if (slot == null || !slot.HasCamera) return;

            var camId = slot.CameraId;
            slot.CameraId = null;

            var cam = AvailableAgents.SelectMany(a => a.Cameras).FirstOrDefault(c => c.Id == camId);
            if (cam != null)
            {
                cam.IsAssigned = false;
            }

            UpdateAssignedCount();
        }
    }
}
