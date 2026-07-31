using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace HeatingCameraSystem.Master.Services
{
    public enum AlarmSeverity { Info, Warning, Error }

    public sealed class AlarmEntry
    {
        public DateTime Time { get; init; }
        public AlarmSeverity Severity { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string TimeText => Time.ToString("HH:mm:ss");
    }

    public static class AlarmSink
    {
        private const int MaxEntries = 100;

        public static ObservableCollection<AlarmEntry> Entries { get; } = new();

        public static void Raise(AlarmSeverity severity, string source, string message)
        {
            void Add()
            {
                Entries.Insert(0, new AlarmEntry
                {
                    Time = DateTime.Now,
                    Severity = severity,
                    Source = source,
                    Message = message
                });
                while (Entries.Count > MaxEntries)
                    Entries.RemoveAt(Entries.Count - 1);

                try
                {
                    _ = AppServices.AlarmHistoryRepo?.InsertAsync(new AlarmHistoryRecord
                    {
                        Timestamp = DateTime.Now,
                        Severity = severity,
                        Source = source,
                        Message = message
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AlarmSink] history insert failed: {ex.Message}");
                }
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Add();
            else
                dispatcher.Invoke(Add);
        }

        public static void Remove(AlarmEntry? entry)
        {
            if (entry is null) return;

            void RemoveEntry() => Entries.Remove(entry);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                RemoveEntry();
            else
                dispatcher.Invoke(RemoveEntry);
        }
    }
}
