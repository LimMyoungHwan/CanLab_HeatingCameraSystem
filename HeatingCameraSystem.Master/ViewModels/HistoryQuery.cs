using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// Pure query helpers for the History screen, extracted so the date-range and filter
    /// logic is unit-testable independently of the static AppServices repositories and WPF.
    /// </summary>
    public static class HistoryQuery
    {
        /// <summary>
        /// WPF DatePicker.SelectedDate strips the time to 00:00:00, so a picked "To" date would
        /// otherwise drop that whole day (query uses Timestamp &lt;= to). Normalize to an inclusive
        /// whole-day range: [From.Date 00:00:00, To.Date 23:59:59].
        /// </summary>
        public static (DateTime From, DateTime To) NormalizeRange(DateTime from, DateTime to)
            => (from.Date, to.Date.AddDays(1).AddSeconds(-1));

        /// <summary>
        /// Maps the capture-source filter combo index (0=All, 1=Recipe, 2=Manual, 3=AgentUi) to a
        /// <see cref="CaptureSource"/> filter, or null for "All". Index order is language-stable.
        /// </summary>
        public static CaptureSource? SourceForIndex(int index) => index switch
        {
            1 => CaptureSource.Recipe,
            2 => CaptureSource.Manual,
            3 => CaptureSource.AgentUi,
            _ => null
        };

        /// <summary>
        /// Applies the camera and source filters shared by the list view and CSV export. A null
        /// <paramref name="cameraId"/> or <paramref name="source"/> means "do not filter on it".
        /// </summary>
        public static IEnumerable<CaptureHistoryRecord> ApplyFilters(
            IEnumerable<CaptureHistoryRecord> records, string? cameraId, CaptureSource? source)
        {
            if (!string.IsNullOrEmpty(cameraId))
                records = records.Where(r => r.CameraId == cameraId);
            if (source.HasValue)
                records = records.Where(r => r.Source == source.Value);
            return records;
        }
    }
}
