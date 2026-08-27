using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>알람 심각도. 실시간 목록(<see cref="AlarmEntry"/>)과 영속 이력(<see cref="AlarmHistoryRecord"/>)이 공유한다.</summary>
    public enum AlarmSeverity { Info, Warning, Error }

    /// <summary>화면 알람 목록에 표시되는 알람 1건. UI 바인딩 전용이며 영속은 <see cref="AlarmHistoryRecord"/>가 맡는다.</summary>
    public sealed class AlarmEntry
    {
        public DateTime Time { get; init; }
        public AlarmSeverity Severity { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string TimeText => Time.ToString("HH:mm:ss");
    }

    /// <summary>
    /// PLC·NATS·카메라 오류가 화면별 임의 메시지 대신 거쳐 가는 공용 알람 채널.
    /// 어느 스레드에서 불러도 되도록 WPF 디스패처로 스스로 마샬링하며,
    /// 실시간 목록은 최신순으로 최대 100건만 유지한다.
    /// </summary>
    public static class AlarmSink
    {
        private const int MaxEntries = 100;

        public static ObservableCollection<AlarmEntry> Entries { get; } = new();

        /// <summary>
        /// 알람을 목록 맨 앞에 추가하고 LiteDB 이력에도 남긴다.
        /// 이력 저장은 fire-and-forget 최선-노력이라 실패해도 알람 표시는 계속된다.
        /// 디스패처가 없으면(테스트 등) 호출 스레드에서 바로 실행한다.
        /// </summary>
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

        /// <summary>실시간 목록에서 항목 하나를 지운다. 영속 이력은 건드리지 않는다.</summary>
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
