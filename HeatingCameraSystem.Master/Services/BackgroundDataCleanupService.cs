using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// 주기적으로 오래된 캡처 이미지와 DB 기록을 정리합니다.
    /// 앱 시작 1분 후 첫 실행, 이후 24시간마다 반복.
    /// </summary>
    public sealed class BackgroundDataCleanupService : IDisposable
    {
        private readonly ICaptureHistoryRepository _historyRepo;
        private readonly string _imageStorageRoot;
        private readonly int _retentionDays;
        private Timer? _timer;

        public BackgroundDataCleanupService(
            ICaptureHistoryRepository historyRepo,
            string imageStorageRoot,
            int retentionDays = 30)
        {
            _historyRepo = historyRepo;
            _imageStorageRoot = imageStorageRoot;
            _retentionDays = retentionDays;
        }

        public void Start()
        {
            _timer = new Timer(
                async _ => await RunCleanupAsync(),
                null,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(24));
        }

        public void Stop() => _timer?.Dispose();

        private async Task RunCleanupAsync()
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            System.Diagnostics.Debug.WriteLine($"[Cleanup] Removing data older than {cutoff:yyyy-MM-dd}");

            // 1. DB 레코드 삭제
            await _historyRepo.DeleteOlderThanAsync(cutoff);

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
