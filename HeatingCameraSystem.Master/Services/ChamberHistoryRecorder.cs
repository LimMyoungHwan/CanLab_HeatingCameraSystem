using System;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// PlcStatusService.Updated(~1초 주기)를 구독해 챔버 온습도/흑체 샘플을
    /// 일정 간격으로만 chamber_history 에 기록한다.
    /// </summary>
    public sealed class ChamberHistoryRecorder : IDisposable
    {
        // ponytail: fixed 30s record interval — no per-deployment knob yet;
        // promote to HardwareSettings if operators need to tune sampling density.
        private const int RecordIntervalSeconds = 30;

        private readonly IChamberHistoryRepository _repo;
        private readonly PlcStatusService _plcStatus;
        private DateTime? _lastRecord;

        public ChamberHistoryRecorder(IChamberHistoryRepository repo, PlcStatusService plcStatus)
        {
            _repo = repo;
            _plcStatus = plcStatus;
            _plcStatus.Updated += OnPlcStatusUpdated;
        }

        /// <summary>
        /// 마지막 기록 시각(last)과 현재(now)를 비교해 이번 스냅샷을 기록해야 하는지 판정.
        /// last 가 없으면(첫 샘플) 항상 기록.
        /// </summary>
        internal static bool ShouldRecord(DateTime? last, DateTime now, int intervalSeconds)
            => last is null || (now - last.Value).TotalSeconds >= intervalSeconds;

        private void OnPlcStatusUpdated(object? sender, PlcStatusSnapshot snapshot)
        {
            var now = DateTime.UtcNow;
            if (!ShouldRecord(_lastRecord, now, RecordIntervalSeconds)) return;
            _lastRecord = now;

            try
            {
                _ = _repo.InsertAsync(new ChamberHistoryRecord
                {
                    Timestamp   = now,
                    Temperature = snapshot.CurrentTemperature,
                    Humidity    = snapshot.CurrentHumidity,
                    BlackBody1  = snapshot.BlackBody1Pv,
                    BlackBody2  = snapshot.BlackBody2Pv
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChamberRecorder] insert failed: {ex.Message}");
            }
        }

        public void Dispose() => _plcStatus.Updated -= OnPlcStatusUpdated;
    }
}
