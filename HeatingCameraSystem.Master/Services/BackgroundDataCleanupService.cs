using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 주기적으로 오래된 캡처 이미지와 DB 기록을 정리한다.
    /// 앱 시작 1분 후 첫 실행, 이후 24시간마다 반복한다.
    /// </summary>
    public sealed class BackgroundDataCleanupService : IDisposable
    {
        private readonly ICaptureHistoryRepository _historyRepo;
        private readonly IChamberHistoryRepository _chamberHistoryRepo;
        private readonly IRecipeMeasurementRepository? _measurementRepo;
        private readonly string _imageStorageRoot;
        private readonly int _retentionDays;
        private Timer? _timer;

        public BackgroundDataCleanupService(
            ICaptureHistoryRepository historyRepo,
            IChamberHistoryRepository chamberHistoryRepo,
            string imageStorageRoot,
            int retentionDays = 30,
            IRecipeMeasurementRepository? measurementRepo = null)
        {
            _historyRepo = historyRepo;
            _chamberHistoryRepo = chamberHistoryRepo;
            _imageStorageRoot = imageStorageRoot;
            _retentionDays = retentionDays;
            _measurementRepo = measurementRepo;
        }

        /// <summary>정리 타이머를 건다. 콜백은 UI와 무관하게 스레드풀에서 실행된다.</summary>
        public void Start()
        {
            _timer = new Timer(
                async _ => await RunCleanupAsync(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(24));
        }

        /// <summary>타이머를 해제해 이후 정리를 멈춘다. <see cref="Dispose"/>와 동일하다.</summary>
        public void Stop() => _timer?.Dispose();

        /// <summary>
        /// 보존 기한을 지난 DB 레코드(캡처·챔버 이력)와 저장 루트 아래의 *.jpg 파일을 지운다.
        /// 파일 기준 시각은 생성 시각(UTC)이며, 잠긴 파일 등 개별 삭제 실패는 무시한다.
        /// </summary>
        private async Task RunCleanupAsync()
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            System.Diagnostics.Debug.WriteLine($"[Cleanup] Removing data older than {cutoff:yyyy-MM-dd}");

            // 1. DB 레코드 삭제
            await _historyRepo.DeleteOlderThanAsync(cutoff);
            await _chamberHistoryRepo.DeleteOlderThanAsync(cutoff);
            if (_measurementRepo != null) await _measurementRepo.DeleteOlderThanAsync(cutoff);

            // 2. 이미지 파일 삭제
            if (Directory.Exists(_imageStorageRoot))
            {
                foreach (var file in Directory.GetFiles(_imageStorageRoot, "*.jpg",
                             SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetCreationTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch
                    {
                        // 파일 잠금 등 무시
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("[Cleanup] Done.");
        }

        public void Dispose() => _timer?.Dispose();
    }
}
