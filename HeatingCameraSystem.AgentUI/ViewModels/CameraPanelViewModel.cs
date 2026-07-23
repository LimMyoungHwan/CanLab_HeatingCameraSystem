using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    /// <summary>
    /// One live-view panel bound to a single <see cref="ICameraRuntime"/>. Frames are converted
    /// to a frozen bitmap on the camera loop thread, then marshaled to the UI thread. Per-camera
    /// isolation: this panel only reflects its own runtime's status/faults.
    /// </summary>
    public partial class CameraPanelViewModel : ObservableObject, IDisposable
    {
        private readonly ICameraRuntime _runtime;
        private readonly Dispatcher _dispatcher;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private string _status;

        public int CameraIndex => _runtime.CameraIndex;

        public CameraPanelViewModel(string title, ICameraRuntime runtime, Dispatcher dispatcher)
        {
            _title = title;
            _runtime = runtime;
            _dispatcher = dispatcher;
            _status = runtime.Status.ToString();

            _runtime.FrameReady += OnFrameReady;
            _runtime.StatusChanged += OnStatusChanged;
        }

        private void OnFrameReady(object? sender, ThermalFrame frame)
        {
            BitmapSource bmp;
            try
            {
                bmp = ThermalFrameBitmapSourceConverter.ToBitmapSource(frame);
            }
            catch
            {
                return;
            }

            _dispatcher.InvokeAsync(() => LiveImage = bmp);
        }

        private void OnStatusChanged(object? sender, CameraRuntimeStatus status)
        {
            _dispatcher.InvokeAsync(() => Status = status.ToString());
        }

        [RelayCommand]
        private async Task RestartAsync()
        {
            await _runtime.StopAsync();
            await _runtime.StartAsync();
        }

        public void Dispose()
        {
            _runtime.FrameReady -= OnFrameReady;
            _runtime.StatusChanged -= OnStatusChanged;
        }
    }
}
