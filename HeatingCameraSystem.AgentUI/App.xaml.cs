using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using HeatingCameraSystem.Protocols.Cameras.CL;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.AgentUI
{
    public partial class App : Application
    {
        // Session-scoped single-instance guard: prevents autostart + Manager relaunch
        // (scheduled task) from double-launching AgentUI in the same operator session.
        private const string SingleInstanceMutexName = "HeatingCameraSystem.AgentUI.SingleInstance";

        private Mutex? _singleInstanceMutex;
        private CameraRuntimeManager? _manager;
        private MainViewModel? _mainViewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another AgentUI instance already owns this session.
                Shutdown();
                return;
            }

            base.OnStartup(e);

            AgentUiConfig config = AgentUiConfig.LoadOrCreate();

            Func<CameraDescriptor, ICameraRuntime> sourceFactory = config.SimulationMode
                ? (d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()))
                : (d => new CameraRuntime(d.OpenCvIndex, new CltcThermalFrameSource(d.OpenCvIndex)));

            _manager = new CameraRuntimeManager(sourceFactory);
            _mainViewModel = new MainViewModel(config.SimulationMode ? "AgentUI — SIMULATION" : "AgentUI");

            Dispatcher dispatcher = Dispatcher;
            foreach (CameraDescriptor cam in config.Cameras)
            {
                ICameraRuntime runtime = _manager.Add(cam);
                _mainViewModel.Cameras.Add(new CameraPanelViewModel(cam.Alias, runtime, dispatcher));
            }

            // Fire-and-forget: per-camera start failures are isolated inside the manager.
            _ = _manager.StartAllAsync();

            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_mainViewModel is not null)
                {
                    foreach (CameraPanelViewModel panel in _mainViewModel.Cameras.ToList())
                    {
                        panel.Dispose();
                    }
                }

                _manager?.Dispose();
            }
            catch
            {
                // best effort during shutdown
            }

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
