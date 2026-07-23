using System;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.AgentUI.ViewModels
{
    public partial class DataBrowserViewModel : ObservableObject
    {
        private readonly CaptureStore _store;

        public ObservableCollection<CaptureRecord> Captures { get; } = new();

        [ObservableProperty]
        private CaptureRecord? _selected;

        [ObservableProperty]
        private BitmapSource? _preview;

        [ObservableProperty]
        private int _retentionDays = 30;

        [ObservableProperty]
        private string _statusText = string.Empty;

        public DataBrowserViewModel(CaptureStore store)
        {
            _store = store;
            Refresh();
        }

        partial void OnSelectedChanged(CaptureRecord? value)
        {
            Preview = null;
            if (value is null)
            {
                return;
            }

            try
            {
                ThermalFrame frame = ThermalFrameReader.Read(value);
                Preview = ThermalFrameBitmapSourceConverter.ToBitmapSource(frame);
            }
            catch (Exception ex)
            {
                StatusText = $"Preview failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Refresh()
        {
            Captures.Clear();
            foreach (CaptureRecord record in _store.Query(limit: 500))
            {
                Captures.Add(record);
            }

            StatusText = $"{Captures.Count} captures";
        }

        [RelayCommand]
        private void DeleteSelected()
        {
            if (Selected is null)
            {
                return;
            }

            if (_store.Delete(Selected.Id))
            {
                Captures.Remove(Selected);
                Selected = null;
                Preview = null;
                StatusText = "Deleted 1 capture";
            }
        }

        [RelayCommand]
        private void Purge()
        {
            int days = Math.Max(0, RetentionDays);
            int removed = _store.Purge(DateTime.UtcNow.AddDays(-days));
            Refresh();
            StatusText = $"Purged {removed} older than {days}d";
        }
    }
}
