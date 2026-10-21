using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;

namespace HeatingCameraSystem.Master.ViewModels
{
    /// <summary>결과 화면 좌측 목록의 실행 회차 1건.</summary>
    public partial class RecipeRunModel : ObservableObject
    {
        public string RunId { get; set; } = string.Empty;
        public string RecipeName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public int MeasurementCount { get; set; }

        public string Display => $"{RecipeName} — {StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }

    /// <summary>측정 로그 그리드의 한 행. 카메라 온도는 여러 대를 한 문자열로 합쳐 보여준다.</summary>
    public partial class MeasurementRowModel : ObservableObject
    {
        public DateTime Timestamp { get; set; }
        public float ChamberTemperature { get; set; }
        public float ChamberHumidity { get; set; }
        public string CameraTemperatures { get; set; } = string.Empty;
    }

    /// <summary>
    /// 레시피 실행 결과 조회 화면. 좌측에서 실행 회차를 고르면 그 회차의 측정 기록과 캡처를 불러온다.
    /// 기록 조건을 쓰지 않은 회차는 측정이 0건이라 캡처 탭만 의미가 있다.
    /// </summary>
    public partial class RecipeResultViewModel : ObservableObject
    {
        public ObservableCollection<RecipeRunModel> Runs { get; } = new();
        public ObservableCollection<MeasurementRowModel> Measurements { get; } = new();
        public ObservableCollection<CaptureHistoryRecord> Captures { get; } = new();

        [ObservableProperty] private RecipeRunModel? _selectedRun;
        [ObservableProperty] private CaptureHistoryRecord? _selectedCapture;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _hasMeasurements;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-6);
        [ObservableProperty] private DateTime _toDate = DateTime.Today;
        [ObservableProperty] private string _histogramSource = string.Empty;
        [ObservableProperty] private string _histogramNote = string.Empty;
        [ObservableProperty] private string _histogramStatistics = string.Empty;
        public ObservableCollection<LegacyRawCandidate> RawCandidates { get; } = new();
        [ObservableProperty] private LegacyRawCandidate? _selectedRawCandidate;
        [ObservableProperty] private string _rawDiscoveryNote = string.Empty;
        private LegacyRawDiscovery? _rawDiscovery;

        /// <summary>차트가 구독한다. 선택 회차의 측정이 다시 로드될 때마다 발생.</summary>
        public event EventHandler<IReadOnlyList<RecipeMeasurementRecord>>? MeasurementsLoaded;

        /// <summary>히스토그램 차트가 구독한다. 도수 배열이 비면 "데이터 없음"을 뜻한다.</summary>
        public event EventHandler<int[]>? HistogramLoaded;

        private List<RecipeMeasurementRecord> _rawMeasurements = new();
        private readonly RawImageClient? _rawClient =
            AppServices.NatsService == null ? null : new RawImageClient(AppServices.NatsService);

        public RecipeResultViewModel()
        {
            _ = LoadRunsAsync();
        }

        partial void OnSelectedRunChanged(RecipeRunModel? value) => _ = LoadRunAsync(value);

        [RelayCommand]
        private async Task RefreshAsync() => await LoadRunsAsync();

        partial void OnSelectedCaptureChanged(CaptureHistoryRecord? value)
        {
            HistogramLoaded?.Invoke(this, Array.Empty<int>());
            HistogramSource = HistogramNote = HistogramStatistics = string.Empty;
            RawCandidates.Clear();
            RawDiscoveryNote = string.Empty;
            _rawDiscovery = null;
            SelectedRawCandidate = null;
        }

        /// <summary>생산 .raw를 우선 읽고, 없는 경우에만 대표 .y16을 사용한다.</summary>
        [RelayCommand]
        private async Task LoadHistogramAsync()
        {
            var capture = SelectedCapture;
            if (capture is null) { StatusMessage = "캡처를 먼저 선택하세요."; return; }
            HistogramLoaded?.Invoke(this, Array.Empty<int>());
            HistogramSource = HistogramNote = HistogramStatistics = string.Empty;
            try
            {
                await DiscoverRawAsync(capture);
                if (capture != SelectedCapture) return;
                ReadImageDimensions(capture);
                var result = await CaptureHistogramSource.LoadAsync(capture, async () =>
                {
                    var (pixels, ok) = await FetchRawAsync(capture);
                    return ok ? pixels : null;
                }, _rawDiscovery);
                if (capture != SelectedCapture || result is null) return;
                HistogramSource = result.Source;
                HistogramNote = result.Note;
                var analysis = result.Analysis;
                HistogramStatistics = $"평균 (Mean): {analysis.Mean:F2} · 표준편차 (std): {analysis.StandardDeviation:F2} · 중앙값 (Median): {analysis.Median:F2} · {analysis.PixelCount:N0} 픽셀";
                HistogramLoaded?.Invoke(this, analysis.Bins);
                StatusMessage = "히스토그램 생성 완료";
            }
            catch (Exception ex)
            {
                if (capture == SelectedCapture) StatusMessage = $"히스토그램 읽기 실패 (.raw가 있으면 .y16으로 대체하지 않습니다): {ex.Message}";
            }
        }

        private async Task DiscoverRawAsync(CaptureHistoryRecord capture)
        {
            if (!string.IsNullOrWhiteSpace(capture.ProductionRawPath) || !string.IsNullOrWhiteSpace(capture.LinkedRawPath)) return;
            LegacyRawContext? context = null;
            if (AppServices.RecipeRepo is not null)
            {
                var recipes = (await AppServices.RecipeRepo.GetAllAsync()).ToArray();
                string? recipeId = _rawMeasurements.FirstOrDefault(m => m.RunId == capture.RunId)?.RecipeId;
                var matches = recipes.Where(r => r.Id == recipeId).ToArray();
                if (matches.Length == 0)
                    matches = recipes.Where(r =>
                    {
                        string? stepId = RecipeCaptureRequestKey.GetStepId(capture.RecipeStepId, r.Steps);
                        return r.Steps.Any(s => s.StepId == stepId);
                    }).ToArray();
                if (matches.Length == 1) context = LegacyRawAssociation.FromRecipe(capture, matches[0]);
            }
            var discovery = await Task.Run(() => LegacyRawAssociation.Discover(capture, context));
            if (capture != SelectedCapture) return;
            _rawDiscovery = discovery;
            RawCandidates.Clear();
            foreach (var candidate in discovery.Candidates) RawCandidates.Add(candidate);
            SelectedRawCandidate = RawCandidates.FirstOrDefault();
            RawDiscoveryNote = discovery.Note;
        }

        private static void ReadImageDimensions(CaptureHistoryRecord capture)
        {
            if (capture.PixelWidth > 0 && capture.PixelHeight > 0) return;
            if (!System.IO.File.Exists(capture.ImagePath)) return;
            try
            {
                using var stream = System.IO.File.OpenRead(capture.ImagePath);
                var image = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                capture.PixelWidth = image.PixelWidth;
                capture.PixelHeight = image.PixelHeight;
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeResult] representative dimensions unavailable: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task SelectProductionRawAsync()
        {
            var capture = SelectedCapture;
            if (capture is null) { StatusMessage = "캡처를 먼저 선택하세요."; return; }
            try
            {
                await DiscoverRawAsync(capture);
                if (capture != SelectedCapture) return;
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "원본 .raw 선택 (운영자 지정으로 저장)",
                    Filter = "원본 RAW (*.raw)|*.raw",
                    CheckFileExists = true,
                    InitialDirectory = SelectedRawCandidate is not null
                        ? System.IO.Path.GetDirectoryName(SelectedRawCandidate.Path) ?? string.Empty
                        : _rawDiscovery?.InitialDirectory ?? System.IO.Path.GetDirectoryName(
                            string.IsNullOrWhiteSpace(capture.LinkedRawPath) ? capture.ProductionRawPath : capture.LinkedRawPath) ?? string.Empty,
                    FileName = SelectedRawCandidate?.Path ?? capture.LinkedRawPath
                };
                if (dialog.ShowDialog() != true) return;
                ReadImageDimensions(capture);
                if (capture.PixelWidth <= 0 || capture.PixelHeight <= 0) await RequestRawAsync(capture);
                await LegacyRawAssociation.SetOperatorLinkAsync(AppServices.HistoryRepo, capture,
                    dialog.FileName, capture.PixelWidth, capture.PixelHeight);
                if (capture == SelectedCapture) await LoadHistogramAsync();
            }
            catch (Exception ex) { StatusMessage = $"원본 .raw 연결 실패: {ex.Message}"; }
        }

        [RelayCommand]
        private async Task ClearProductionRawLinkAsync()
        {
            var capture = SelectedCapture;
            if (capture is null) { StatusMessage = "캡처를 먼저 선택하세요."; return; }
            try
            {
                await LegacyRawAssociation.SetOperatorLinkAsync(AppServices.HistoryRepo, capture,
                    string.Empty, capture.PixelWidth, capture.PixelHeight);
                if (capture == SelectedCapture) await LoadHistogramAsync();
            }
            catch (Exception ex) { StatusMessage = $"원본 .raw 연결 해제 실패: {ex.Message}"; }
        }

        /// <summary>선택 캡처의 원본을 Agent에서 받아 .y16 파일로 저장한다.</summary>
        [RelayCommand]
        private async Task ExportRawAsync()
        {
            var (pixels, ok) = await FetchRawAsync();
            if (!ok || pixels == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Raw 16bit (*.y16)|*.y16",
                FileName = $"{SelectedCapture?.CameraId}_{SelectedCapture?.Timestamp.ToLocalTime():yyyyMMdd_HHmmss}.y16"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, pixels);
                StatusMessage = $"내보내기 완료: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"내보내기 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// 선택 회차의 모든 캡처 원본을 Agent에서 순차로 받아 로컬에 저장한다.
        /// 이미 받아 둔 파일은 건너뛰므로 중단 후 다시 눌러도 남은 것만 받는다.
        /// </summary>
        [RelayCommand]
        private async Task CollectRawAsync()
        {
            if (Captures.Count == 0) { StatusMessage = "수집할 캡처가 없습니다."; return; }
            if (_rawClient == null) { StatusMessage = "NATS가 초기화되지 않아 원본을 가져올 수 없습니다."; return; }

            IsBusy = true;
            int done = 0, skipped = 0, failed = 0;
            try
            {
                var targets = Captures.ToList();
                for (int i = 0; i < targets.Count; i++)
                {
                    StatusMessage = $"데이터 수집 중... {i + 1}/{targets.Count} (완료 {done} / 보유 {skipped} / 실패 {failed})";

                    string path = RawCachePath(targets[i]);
                    if (path.Length > 0 && System.IO.File.Exists(path)) { skipped++; continue; }

                    var (pixels, ok) = await RequestRawAsync(targets[i]);
                    if (ok && pixels != null) done++; else failed++;
                }
            }
            finally
            {
                IsBusy = false;
                StatusMessage = $"데이터 수집 완료: 신규 {done}건 / 기보유 {skipped}건 / 실패 {failed}건";
            }
        }

        /// <summary>선택 캡처의 원본. 로컬에 받아 둔 파일이 있으면 Agent에 묻지 않는다.</summary>
        private async Task<(byte[]? Pixels, bool Ok)> FetchRawAsync(CaptureHistoryRecord? capture = null)
        {
            capture ??= SelectedCapture;
            if (capture == null) { StatusMessage = "캡처를 먼저 선택하세요."; return (null, false); }

            string path = RawCachePath(capture);
            if (path.Length > 0 && System.IO.File.Exists(path))
            {
                try { return (await System.IO.File.ReadAllBytesAsync(path), true); }
                catch (Exception ex) { StatusMessage = $"로컬 원본 읽기 실패: {ex.Message}"; return (null, false); }
            }

            return await RequestRawAsync(capture);
        }

        /// <summary>Agent에 원본을 요청하고 성공하면 로컬 캐시에 저장한다.</summary>
        private async Task<(byte[]? Pixels, bool Ok)> RequestRawAsync(CaptureHistoryRecord capture)
        {
            if (_rawClient == null) { StatusMessage = "NATS가 초기화되지 않아 원본을 가져올 수 없습니다."; return (null, false); }

            bool wasBusy = IsBusy;
            IsBusy = true;
            if (!wasBusy) StatusMessage = "Agent에서 원본을 가져오는 중...";
            try
            {
                var response = await _rawClient.RequestAsync(capture.AgentId, capture.AgentRawPath);
                if (!response.IsSuccess || response.Pixels == null)
                {
                    StatusMessage = string.IsNullOrWhiteSpace(response.Message) ? "원본을 가져오지 못했습니다." : response.Message;
                    return (null, false);
                }

                capture.PixelWidth = response.Width;
                capture.PixelHeight = response.Height;
                SaveRawCache(capture, response.Pixels);
                return (response.Pixels, true);
            }
            finally
            {
                IsBusy = wasBusy;
            }
        }

        /// <summary>
        /// 원본 로컬 보관 경로: <c>{ImageCacheDir}\raw\{RunId}\{CaptureId}.y16</c>.
        /// 회차 없는 캡처(수동/AgentUI)는 묶을 폴더가 없으므로 빈 문자열 = 보관하지 않는다.
        /// </summary>
        private static string RawCachePath(CaptureHistoryRecord capture)
        {
            if (string.IsNullOrEmpty(AppServices.ImageCacheDir)) return string.Empty;
            if (string.IsNullOrEmpty(capture.RunId)) return string.Empty;
            return System.IO.Path.Combine(AppServices.ImageCacheDir, "raw", capture.RunId, $"{capture.Id}.y16");
        }

        private void SaveRawCache(CaptureHistoryRecord capture, byte[] pixels)
        {
            string path = RawCachePath(capture);
            if (path.Length == 0) return;

            try
            {
                string? directory = System.IO.Path.GetDirectoryName(path);
                if (directory is null) return;
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(path, pixels);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecipeResult] raw cache write failed: {ex.Message}");
            }
        }

        private async Task LoadRunsAsync()
        {
            Runs.Clear();
            var repo = AppServices.RecipeMeasurementRepo;
            if (repo == null) { StatusMessage = "측정 저장소가 초기화되지 않았습니다."; return; }

            try
            {
                var from = DateTime.SpecifyKind(FromDate.Date, DateTimeKind.Local);
                var to = DateTime.SpecifyKind(ToDate.Date.AddDays(1), DateTimeKind.Local);
                foreach (var run in BuildRuns(await repo.QueryBetweenAsync(from, to)))
                    Runs.Add(run);

                StatusMessage = Runs.Count == 0 ? "선택한 기간에 기록된 실행 회차가 없습니다." : string.Empty;
                SelectedRun = Runs.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusMessage = $"회차 목록 조회 실패: {ex.Message}";
            }
        }

        // ponytail: 기간 경계에 걸친 회차는 기간 안쪽 측정만 집계된다. 문제 되면 RunId로 전체를 다시 조회.
        internal static List<RecipeRunModel> BuildRuns(IEnumerable<RecipeMeasurementRecord> samples)
            => samples
                .GroupBy(s => s.RunId, StringComparer.Ordinal)
                .Select(g => new RecipeRunModel
                {
                    RunId = g.Key,
                    RecipeName = g.First().RecipeName,
                    StartedAt = g.Min(s => s.Timestamp),
                    MeasurementCount = g.Count()
                })
                .OrderByDescending(r => r.StartedAt)
                .ToList();

        private async Task LoadRunAsync(RecipeRunModel? run)
        {
            Measurements.Clear();
            Captures.Clear();
            _rawMeasurements = new List<RecipeMeasurementRecord>();
            HasMeasurements = false;

            if (run == null)
            {
                MeasurementsLoaded?.Invoke(this, _rawMeasurements);
                return;
            }

            try
            {
                _rawMeasurements = (await AppServices.RecipeMeasurementRepo.QueryByRunAsync(run.RunId)).ToList();
                foreach (var m in _rawMeasurements)
                {
                    Measurements.Add(new MeasurementRowModel
                    {
                        Timestamp = m.Timestamp,
                        ChamberTemperature = m.ChamberTemperature,
                        ChamberHumidity = m.ChamberHumidity,
                        CameraTemperatures = string.Join(", ", m.CameraTemperatures.Select(kv => $"{kv.Key}={kv.Value:F1}℃"))
                    });
                }
                HasMeasurements = _rawMeasurements.Count > 0;

                foreach (var c in await AppServices.HistoryRepo.QueryByRunAsync(run.RunId))
                    Captures.Add(c);

                StatusMessage = $"측정 {_rawMeasurements.Count}건 / 캡처 {Captures.Count}건";
            }
            catch (Exception ex)
            {
                StatusMessage = $"회차 조회 실패: {ex.Message}";
            }

            MeasurementsLoaded?.Invoke(this, _rawMeasurements);
        }
    }
}
