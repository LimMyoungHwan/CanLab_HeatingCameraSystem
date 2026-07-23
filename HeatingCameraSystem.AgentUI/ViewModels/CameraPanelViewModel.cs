using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    public partial class CameraPanelViewModel : ObservableObject, IDisposable
    {
        private readonly ICameraRuntime _runtime;
        private readonly Dispatcher _dispatcher;
        private readonly ICameraSerialClient? _serial;
        private readonly ThermalNucCorrector _nuc;

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

        public CameraPanelViewModel(string title, ICameraRuntime runtime, Dispatcher dispatcher, ThermalNucCorrector nuc, ICameraSerialClient? serial = null)
        {
            _title = title;
            _runtime = runtime;
            _dispatcher = dispatcher;
            _nuc = nuc;
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
                bmp = ThermalFrameBitmapSourceConverter.ToBitmapSource(_nuc.Apply(frame));
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

        [RelayCommand(CanExecute = nameof(HasSerialControl))]
        private async Task RunNucAsync()
        {
            if (_serial is null) return;
            try
            {
                SerialStatus = "NUC: 셔터 닫고 평면필드 캡처…";
                await _serial.SetShutterAsync(false);
                await Task.Delay(400);

                ThermalFrame? flat = await AverageFramesAsync(12);
                await _serial.SetShutterAsync(true);

                if (flat is null)
                {
                    SerialStatus = "NUC 실패: 프레임 없음";
                    return;
                }

                _nuc.CaptureFromFlat(flat);
                SerialStatus = $"NUC 완료 (데드픽셀 {_nuc.DeadPixelCount}개) {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SerialStatus = $"NUC 오류: {ex.Message}";
                try { await _serial.SetShutterAsync(true); } catch { }
            }
        }

        private async Task<ThermalFrame?> AverageFramesAsync(int frameCount)
        {
            ThermalFrame? first = _runtime.LatestFrame;
            if (first is null) return null;

            int len = first.Pixels.Length;
            var acc = new long[len];
            int used = 0;
            for (int k = 0; k < frameCount; k++)
            {
                ThermalFrame? f = _runtime.LatestFrame;
                if (f is not null && f.Pixels.Length == len)
                {
                    for (int i = 0; i < len; i++) acc[i] += f.Pixels[i] & 0x3FFF;
                    used++;
                }
                await Task.Delay(35);
            }

            if (used == 0) return null;

            var avg = new ushort[len];
            for (int i = 0; i < len; i++) avg[i] = (ushort)(acc[i] / used);
            return new ThermalFrame(avg, first.Width, first.Height, DateTimeOffset.Now);
        }

        /// <summary>영상 시작: 카메라 RUN 후 셔터 열기 (기본 셔터 닫힘 → 실 열영상 취득). 앱 시작 시 자동 호출.</summary>
        public Task StartLiveAsync() => RunSerialAsync(async s =>
        {
            await s.SetCameraRunningAsync(true);
            await s.SetShutterAsync(true);
        }, "영상 시작 (RUN+셔터 열림)");

        /// <summary>영상 종료: 셔터 닫기 후 카메라 STOP. 앱 종료 시 자동 호출.</summary>
        public Task StopLiveAsync() => RunSerialAsync(async s =>
        {
            await s.SetShutterAsync(false);
            await s.SetCameraRunningAsync(false);
        }, "영상 종료 (셔터 닫힘+STOP)");

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
