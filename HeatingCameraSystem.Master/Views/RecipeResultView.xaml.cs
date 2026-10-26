using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Master.ViewModels;

namespace HeatingCameraSystem.Master.Views
{
    /// <summary>
    /// 결과 화면. ScottPlot은 데이터 바인딩 대상이 아니라 명령형 API라, ViewModel이 올리는
    /// MeasurementsLoaded 이벤트를 받아 코드비하인드에서 다시 그린다.
    /// </summary>
    public partial class RecipeResultView : UserControl
    {
        private RecipeResultViewModel? _boundViewModel;
        private const string ChartFontName = "Malgun Gothic";

        public RecipeResultView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) => Detach();
        }

        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            Detach();
            if (DataContext is not RecipeResultViewModel vm) return;
            _boundViewModel = vm;
            vm.MeasurementsLoaded += OnMeasurementsLoaded;
            vm.HistogramLoaded += OnHistogramLoaded;
        }

        private void Detach()
        {
            if (_boundViewModel == null) return;
            _boundViewModel.MeasurementsLoaded -= OnMeasurementsLoaded;
            _boundViewModel.HistogramLoaded -= OnHistogramLoaded;
            _boundViewModel = null;
        }

        private void OnHistogramLoaded(object? sender, HistogramPlotData data)
        {
            Histogram.Plot.Clear();
            Histogram.Plot.HideLegend();
            // 참조 도구와 비교하기 쉽도록 히스토그램 축과 Center/std 범례는 영문 표기를 유지한다.

            int[] bins = data.Bins;
            if (bins.Length > 0)
            {
                double binWidth = (RawHistogram.MaxPixelValue + 1) / (double)bins.Length;
                double[] xs = Enumerable.Range(0, bins.Length).Select(i => (i + 0.5) * binWidth).ToArray();

                Histogram.Plot.Add.Bars(xs, bins.Select(b => (double)b).ToArray());
                Histogram.Plot.Axes.Bottom.Label.Text = "Pixel Intensity";
                Histogram.Plot.Axes.Left.Label.Text = "Count";

                var center = Histogram.Plot.Add.VerticalLine(data.Mean);
                center.Color = ScottPlot.Colors.Red;
                center.LineWidth = 2;
                center.LinePattern = ScottPlot.LinePattern.Dashed;
                center.LegendText = string.Create(CultureInfo.InvariantCulture, $"Center: {data.Mean:F0}, std:{data.StandardDeviation:F4}");
                Histogram.Plot.ShowLegend();
                Histogram.Plot.Axes.AutoScale();
            }

            // ScottPlot은 WPF FontFamily를 상속하지 않으므로 차트 글꼴을 직접 설정한다.
            Histogram.Plot.Font.Set(ChartFontName);
            Histogram.Refresh();
        }

        private void OnMeasurementsLoaded(object? sender, IReadOnlyList<RecipeMeasurementRecord> samples)
        {
            Chart.Plot.Clear();

            if (samples.Count > 0)
            {
                double[] xs = samples.Select(s => s.Timestamp.ToLocalTime().ToOADate()).ToArray();

                var temp = Chart.Plot.Add.Scatter(xs, samples.Select(s => (double)s.ChamberTemperature).ToArray());
                temp.LegendText = "챔버 온도(℃)";

                var humidity = Chart.Plot.Add.Scatter(xs, samples.Select(s => (double)s.ChamberHumidity).ToArray());
                humidity.LegendText = "챔버 습도(%RH)";

                // 카메라는 대수가 가변이라 회차 전체에 한 번이라도 등장한 AgentId만 계열로 만든다.
                foreach (string agentId in samples.SelectMany(s => s.CameraTemperatures.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
                {
                    double[] ys = samples
                        .Select(s => s.CameraTemperatures.TryGetValue(agentId, out double v) ? v : double.NaN)
                        .ToArray();

                    var camera = Chart.Plot.Add.Scatter(xs, ys);
                    camera.LegendText = $"{agentId} 카메라(℃)";
                }

                Chart.Plot.Axes.DateTimeTicksBottom();
                Chart.Plot.ShowLegend();
                Chart.Plot.Axes.AutoScale();
            }

            Chart.Plot.Font.Set(ChartFontName);
            Chart.Refresh();
        }
    }
}
