using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    public partial class LiveCameraTile : ObservableObject
    {
        [ObservableProperty] private string _key = string.Empty;
        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private BitmapSource? _liveImage;
    }

    public partial class LiveViewModel : ObservableObject
    {
        public ObservableCollection<LiveCameraTile> Cameras { get; } = new();

        public LiveViewModel()
        {
            _ = SubscribeLiveFramesAsync();
        }

        private async Task SubscribeLiveFramesAsync()
        {
            if (AppServices.NatsService is null) return;
            try
            {
                await AppServices.NatsService.SubscribeLiveFrameAsync(OnLiveFrame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveView] subscribe failed: {ex.Message}");
            }
        }

        private void OnLiveFrame(LiveFrameMessage msg)
        {
            if (msg.ImageBytes is null || msg.ImageBytes.Length == 0) return;

            BitmapSource? bmp = Decode(msg.ImageBytes);
            if (bmp is null) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                string key = $"{msg.AgentId}#{msg.CameraIndex}";
                LiveCameraTile? tile = null;
                foreach (LiveCameraTile t in Cameras)
                {
                    if (t.Key == key) { tile = t; break; }
                }

                if (tile is null)
                {
                    tile = new LiveCameraTile { Key = key, Title = $"{msg.AgentId} (cam {msg.CameraIndex})" };
                    Cameras.Add(tile);
                }

                tile.LiveImage = bmp;
            });
        }

        private static BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                var bmp = new BitmapImage();
                using var ms = new MemoryStream(jpeg);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
