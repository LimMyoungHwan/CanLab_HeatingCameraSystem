using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    public partial class CameraRow : ObservableObject
    {
        [ObservableProperty]
        private string _agentId;

        [ObservableProperty]
        private int _openCvIndex;

        [ObservableProperty]
        private string _alias;

        public CameraRow(CameraDescriptor descriptor)
        {
            _agentId = descriptor.AgentId;
            _openCvIndex = descriptor.OpenCvIndex;
            _alias = descriptor.Alias;
        }

        public CameraRow()
        {
            _agentId = "Camera";
            _openCvIndex = 0;
            _alias = "Camera";
        }

        public CameraDescriptor ToDescriptor() => new(AgentId, OpenCvIndex, Alias);
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly AgentUiConfig _config;

        [ObservableProperty]
        private bool _simulationMode;

        [ObservableProperty]
        private string _natsUrl;

        [ObservableProperty]
        private string _storagePath;

        [ObservableProperty]
        private int _heartbeatSeconds;

        [ObservableProperty]
        private string _statusText = string.Empty;

        public ObservableCollection<CameraRow> Cameras { get; } = new();

        public SettingsViewModel(AgentUiConfig config)
        {
            _config = config;
            _simulationMode = config.SimulationMode;
            _natsUrl = config.NatsUrl;
            _storagePath = config.StoragePath;
            _heartbeatSeconds = config.HeartbeatSeconds;

            foreach (CameraDescriptor camera in config.Cameras)
            {
                Cameras.Add(new CameraRow(camera));
            }
        }

        [RelayCommand]
        private void AddCamera() => Cameras.Add(new CameraRow());

        [RelayCommand]
        private void RemoveCamera(CameraRow? row)
        {
            if (row is not null)
            {
                Cameras.Remove(row);
            }
        }

        [RelayCommand]
        private void Save()
        {
            _config.SimulationMode = SimulationMode;
            _config.NatsUrl = NatsUrl;
            _config.StoragePath = StoragePath;
            _config.HeartbeatSeconds = HeartbeatSeconds;
            _config.Cameras = Cameras.Select(row => row.ToDescriptor()).ToList();

            try
            {
                _config.Save();
                StatusText = "Saved. Restart AgentUI to apply.";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }
    }
}
