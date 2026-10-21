using System;

namespace HeatingCameraSystem.Master.Services
{
    public record HistogramAnalysis(int[] Bins, int PixelCount, double Mean, double StandardDeviation, double Median);

    /// <summary>
    /// .y16 원본 바이트열의 히스토그램을 만든다. 픽셀은 리틀엔디안 16비트이며,
    /// 카메라가 14비트를 담아 보내므로 상한은 16383이다.
    /// </summary>
    public static class RawHistogram
    {
        public const int MaxPixelValue = 16383;

        /// <summary>
        /// binCount개의 균등 구간으로 나눈 도수를 돌려준다. 홀수 바이트(잘린 마지막 픽셀)는 버린다.
        /// 값이 상한을 넘으면 마지막 구간에 몰아넣어 인덱스가 벗어나지 않게 한다.
        /// </summary>
        public static int[] Compute(byte[] pixels, int binCount = 256)
        {
            if (binCount < 1) throw new ArgumentOutOfRangeException(nameof(binCount));

            var bins = new int[binCount];
            if (pixels == null || pixels.Length < 2) return bins;

            int usable = pixels.Length - (pixels.Length % 2);
            double scale = (double)binCount / (MaxPixelValue + 1);

            for (int i = 0; i < usable; i += 2)
            {
                int value = pixels[i] | (pixels[i + 1] << 8);
                int bin = (int)(value * scale);
                if (bin >= binCount) bin = binCount - 1;
                bins[bin]++;
            }

            return bins;
        }

        /// <summary>
        /// Analyzes unshifted little-endian ushort samples. Histogram bins clamp values above
        /// 16383, but statistics retain the original values and use population standard deviation.
        /// With excludeFirstPixel, the first sample is FPA metadata: the buffer must contain
        /// complete samples and at least one image sample, otherwise ArgumentException is thrown.
        /// Otherwise, trailing odd bytes are ignored and empty input returns zero statistics,
        /// matching the legacy histogram's permissive input handling.
        /// </summary>
        public static HistogramAnalysis Analyze(byte[] pixels, bool excludeFirstPixel = false, int binCount = 256)
        {
            if (binCount < 1) throw new ArgumentOutOfRangeException(nameof(binCount));
            if (excludeFirstPixel && (pixels == null || pixels.Length < 4 || pixels.Length % 2 != 0))
                throw new ArgumentException("Raw data must contain complete ushort samples, including FPA and at least one image pixel.", nameof(pixels));

            var bins = new int[binCount];
            int start = excludeFirstPixel ? 2 : 0;
            int count = pixels == null ? 0 : pixels.Length / 2 - start / 2;
            if (pixels == null || count == 0)
                return new HistogramAnalysis(bins, 0, 0, 0, 0);

            // Full ushort frequencies retain exact median values without sorting/copying pixels.
            var frequencies = new int[ushort.MaxValue + 1];
            double scale = (double)binCount / (MaxPixelValue + 1);
            long sum = 0;
            long sumSquares = 0;
            for (int i = start; i + 1 < pixels.Length; i += 2)
            {
                int value = pixels[i] | (pixels[i + 1] << 8);
                int bin = (int)(value * scale);
                if (bin >= binCount) bin = binCount - 1;
                bins[bin]++;
                frequencies[value]++;
                sum += value;
                sumSquares += (long)value * value;
            }

            int lowerRank = (count - 1) / 2;
            int upperRank = count / 2;
            int cumulative = 0;
            int lower = 0;
            int upper = 0;
            for (int value = 0; value < frequencies.Length; value++)
            {
                int next = cumulative + frequencies[value];
                if (cumulative <= lowerRank && next > lowerRank) lower = value;
                if (next > upperRank)
                {
                    upper = value;
                    break;
                }
                cumulative = next;
            }

            double mean = (double)sum / count;
            double variance = Math.Max(0, (double)sumSquares / count - mean * mean);
            return new HistogramAnalysis(bins, count, mean, Math.Sqrt(variance), (lower + upper) / 2.0);
        }
    }
}
