using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
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
        private CaptureStore? _store;
        private INatsCommunicationService? _nats;
        private CameraNatsConnector? _natsConnector;

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

            AgentUiLog.Initialize();

            AgentUiConfig config = AgentUiConfig.LoadOrCreate();

            Func<CameraDescriptor, ICameraRuntime> sourceFactory = config.SimulationMode
                ? (d => new CameraRuntime(d.OpenCvIndex, new FakeThermalFrameSource()))
                : (d => new CameraRuntime(d.OpenCvIndex, new CltcThermalFrameSource(d.OpenCvIndex)));

            Func<CameraDescriptor, ICameraSerialClient?> serialFactory = config.SimulationMode
                ? (d => string.IsNullOrWhiteSpace(d.SerialPortName) ? null : new FakeCameraSerialClient(d.SerialPortName!))
                : (d => string.IsNullOrWhiteSpace(d.SerialPortName) ? null : new ClSerialCameraClient(d.SerialPortName!));

            _manager = new CameraRuntimeManager(sourceFactory);
            _mainViewModel = new MainViewModel(config.SimulationMode ? "AgentUI — SIMULATION" : "AgentUI");

            Dispatcher dispatcher = Dispatcher;
            foreach (CameraDescriptor cam in config.Cameras)
            {
                ICameraRuntime runtime = _manager.Add(cam);
                string agentId = cam.AgentId;
                int cameraIndex = cam.OpenCvIndex;
                runtime.StatusChanged += (_, status) =>
                {
                    if (status == CameraRuntimeStatus.Faulted)
                    {
                        AgentUiLog.Logger.Error("Camera {AgentId} (index {Index}) faulted", agentId, cameraIndex);
                    }
                };

                ICameraSerialClient? serial = serialFactory(cam);
                if (serial is not null)
                {
                    try
                    {
                        _ = serial.InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        AgentUiLog.Logger.Warning(ex, "Camera {AgentId} serial {Port} open failed", agentId, cam.SerialPortName);
                        serial.Dispose();
                        serial = null;
                    }
                }

                _mainViewModel.Cameras.Add(new CameraPanelViewModel(cam.Alias, runtime, dispatcher, serial));
            }

            // Fire-and-forget: per-camera start failures are isolated inside the manager.
            _ = _manager.StartAllAsync();

            string storageDir = config.EffectiveStorageDir;
            Directory.CreateDirectory(storageDir);
            _store = new CaptureStore(storageDir, new LiteDbCaptureIndex(Path.Combine(storageDir, "index.db")), config.CaptureImageFormat);

            _nats = new NatsCommunicationService();
            _natsConnector = new CameraNatsConnector(_nats, _manager, _store, config.Cameras, config.HeartbeatSeconds);
            _natsConnector.Start(config.NatsUrl);

            AgentUiLog.Logger.Information(
                "AgentUI started: {CameraCount} cameras, simulation={Simulation}, nats={NatsUrl}",
                config.Cameras.Count, config.SimulationMode, config.NatsUrl);

            _mainViewModel.DataBrowser = new DataBrowserViewModel(_store);
            _mainViewModel.Logs = new LogViewerViewModel(AgentUiLog.LogDir);
            _mainViewModel.Settings = new SettingsViewModel(config);

            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _natsConnector?.DisposeAsync().AsTask().GetAwaiter().GetResult();

                if (_mainViewModel is not null)
                {
                    foreach (CameraPanelViewModel panel in _mainViewModel.Cameras.ToList())
                    {
                        panel.Dispose();
                    }
                }

                _manager?.Dispose();
                _store?.Dispose();
                _nats?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // best effort during shutdown
            }

            _singleInstanceMutex?.Dispose();
            AgentUiLog.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
