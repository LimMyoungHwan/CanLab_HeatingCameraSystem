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
    public partial class CameraPanelViewModel : ObservableObject, IDisposable
    {
        private readonly ICameraRuntime _runtime;
        private readonly Dispatcher _dispatcher;
        private readonly ICameraSerialClient? _serial;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private BitmapSource? _liveImage;

        [ObservableProperty]
        private string _status;

        [ObservableProperty]
        private string _serialNumber = "—";

        [ObservableProperty]
        private string _fpaTemperature = "—";

        [ObservableProperty]
        private string _serialStatus = string.Empty;

        public bool HasSerialControl => _serial is not null;

        public int CameraIndex => _runtime.CameraIndex;

        public CameraPanelViewModel(string title, ICameraRuntime runtime, Dispatcher dispatcher, ICameraSerialClient? serial = null)
        {
            _title = title;
            _runtime = runtime;
            _dispatcher = dispatcher;
            _serial = serial;
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

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task OpenShutterAsync() => RunSerialAsync(s => s.SetShutterAsync(true), "셔터 열림");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task CloseShutterAsync() => RunSerialAsync(s => s.SetShutterAsync(false), "셔터 닫힘");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task RunCameraAsync() => RunSerialAsync(s => s.SetCameraRunningAsync(true), "카메라 RUN");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task StopCameraAsync() => RunSerialAsync(s => s.SetCameraRunningAsync(false), "카메라 STOP");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private Task SaveConfigAsync() => RunSerialAsync(s => s.SaveConfigAsync(), "설정 저장");

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private async Task RefreshInfoAsync()
        {
            if (_serial is null) return;
            try
            {
                SerialNumber = await _serial.ReadSerialNumberAsync();
                double fpa = await _serial.ReadFpaTemperatureAsync();
                FpaTemperature = $"{fpa:F1} ℃";
                SerialStatus = $"정보 갱신 {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SerialStatus = $"읽기 실패: {ex.Message}";
            }
        }

        private async Task RunSerialAsync(Func<ICameraSerialClient, Task> action, string label)
        {
            if (_serial is null) return;
            try
            {
                await action(_serial);
                SerialStatus = $"{label} 적용됨";
            }
            catch (Exception ex)
            {
                SerialStatus = $"{label} 오류: {ex.Message}";
            }
        }

        public void Dispose()
        {
            _runtime.FrameReady -= OnFrameReady;
            _runtime.StatusChanged -= OnStatusChanged;
            _serial?.Dispose();
        }
    }
}
