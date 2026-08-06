using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    // 비레시피 캡처(수동/AgentUI)만 capture_history에 기록. 레시피 캡처는 RecipeEngine이 동기 PLC
    // 온습도까지 채워 직접 기록하므로 여기선 무시(RecipeStepId 있음 or Source==Recipe) — 중복 방지.
    public sealed class CaptureResultHistoryRecorder : IDisposable
    {
        private readonly ICaptureHistoryRepository _repo;
        private readonly string _imageCacheDir;
        private readonly Func<PlcStatusSnapshot?> _snapshot;

        // 여러 Agent의 NATS 콜백이 동시 도착 → 이미지 캐시 쓰기 + LiteDB INSERT 직렬화.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public CaptureResultHistoryRecorder(
            ICaptureHistoryRepository repo, string imageCacheDir, Func<PlcStatusSnapshot?> snapshot)
        {
            _repo = repo;
            _imageCacheDir = imageCacheDir;
            _snapshot = snapshot;
        }

        public async Task RecordAsync(CaptureResultMessage result)
        {
            if (!result.IsSuccess) return;
            if (result.Source == CaptureSource.Recipe || !string.IsNullOrEmpty(result.RecipeStepId)) return;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                string imagePath = CaptureResultImageCache.Store(result, _imageCacheDir) ?? result.ImagePath;
                PlcStatusSnapshot? s = _snapshot();

                await _repo.InsertAsync(new CaptureHistoryRecord
                {
                    Id = string.IsNullOrEmpty(result.CaptureId) ? Guid.NewGuid().ToString() : result.CaptureId,
                    CameraId = !string.IsNullOrWhiteSpace(result.Alias) ? result.Alias : result.AgentId,
                    AgentId = result.AgentId,
                    CameraAlias = result.Alias,
                    CameraIndex = result.CameraIndex > 0 ? result.CameraIndex : (int?)null,
                    Source = result.Source,
                    Timestamp = result.Timestamp,
                    Temperature = s?.CurrentTemperature ?? 0f,
                    Humidity = s?.CurrentHumidity ?? 0f,
                    ImagePath = imagePath
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureRecorder] record failed: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose() => _gate.Dispose();
    }
}
