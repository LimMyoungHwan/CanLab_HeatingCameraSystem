using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>
    /// 이력 화면의 순수 쿼리 헬퍼. 날짜 범위·필터 로직을 정적 AppServices 저장소와 WPF에
    /// 의존하지 않고 단위 테스트할 수 있도록 분리한 것이다.
    /// </summary>
    public static class HistoryQuery
    {
        /// <summary>
        /// WPF DatePicker.SelectedDate는 시각을 00:00:00으로 잘라 버리므로, 그대로 두면 선택한
        /// "To" 날짜 하루가 통째로 빠진다(쿼리는 Timestamp &lt;= to 조건). 하루 전체를 포함하는
        /// 범위 [From.Date 00:00:00, To.Date 23:59:59]로 정규화한다.
        /// </summary>
        public static (DateTime From, DateTime To) NormalizeRange(DateTime from, DateTime to)
            => (from.Date, to.Date.AddDays(1).AddSeconds(-1));

        /// <summary>
        /// 촬영 구분 필터 콤보 인덱스(0=전체, 1=Recipe, 2=Manual, 3=AgentUi)를
        /// <see cref="CaptureSource"/> 필터로 변환한다. "전체"는 null이다. 인덱스 순서는 언어와 무관하게 고정이다.
        /// </summary>
        public static CaptureSource? SourceForIndex(int index) => index switch
        {
            1 => CaptureSource.Recipe,
            2 => CaptureSource.Manual,
            3 => CaptureSource.AgentUi,
            _ => null
        };

        /// <summary>
        /// 목록 화면과 CSV 내보내기가 공유하는 카메라·촬영 구분 필터를 적용한다.
        /// <paramref name="cameraId"/> 또는 <paramref name="source"/>가 null이면 해당 조건은 걸지 않는다.
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
