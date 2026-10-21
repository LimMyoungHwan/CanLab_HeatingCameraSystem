using System;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class RawHistogramTests
    {
        [Fact]
        public void Compute_EmptyOrTooShort_ReturnsAllZeroBins()
        {
            Assert.All(RawHistogram.Compute(Array.Empty<byte>(), 8), c => Assert.Equal(0, c));
            Assert.All(RawHistogram.Compute(new byte[] { 0x01 }, 8), c => Assert.Equal(0, c));
        }

        [Fact]
        public void Compute_ReadsLittleEndianPixels()
        {
            // 0x0100 = 256, 0x0001 = 1 - 바이트 순서를 뒤집어 읽으면 다른 구간에 떨어진다.
            int[] bins = RawHistogram.Compute(new byte[] { 0x00, 0x01, 0x01, 0x00 }, RawHistogram.MaxPixelValue + 1);

            Assert.Equal(1, bins[256]);
            Assert.Equal(1, bins[1]);
        }

        [Fact]
        public void Compute_ClampsValuesAboveRangeIntoLastBin()
        {
            // 0xFFFF = 65535 는 14비트 상한(16383)을 넘는다. 인덱스가 벗어나지 않고 마지막 구간에 들어가야 한다.
            int[] bins = RawHistogram.Compute(new byte[] { 0xFF, 0xFF }, 16);

            Assert.Equal(1, bins[^1]);
        }

        [Fact]
        public void Compute_IgnoresTrailingOddByte()
        {
            int[] bins = RawHistogram.Compute(new byte[] { 0x01, 0x00, 0x7F }, 16);

            Assert.Equal(1, bins[0]);
        }

        [Fact]
        public void Compute_RejectsNonPositiveBinCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RawHistogram.Compute(new byte[] { 0x00, 0x00 }, 0));
        }

        [Fact]
        public void Analyze_ReadsUnshiftedLittleEndianSamplesAndPopulationStatistics()
        {
            var result = RawHistogram.Analyze(new byte[] { 0x00, 0x01, 0x01, 0x00 }, binCount: 16384);

            Assert.Equal(2, result.PixelCount);
            Assert.Equal(1, result.Bins[256]);
            Assert.Equal(1, result.Bins[1]);
            Assert.Equal(128.5, result.Mean);
            Assert.Equal(127.5, result.StandardDeviation);
            Assert.Equal(128.5, result.Median);
        }

        [Fact]
        public void Analyze_ExcludesFirstFpaSampleFromBinsAndStatistics()
        {
            var result = RawHistogram.Analyze(
                new byte[] { 0xFF, 0xFF, 0x02, 0x00, 0x04, 0x00, 0x09, 0x00 },
                excludeFirstPixel: true, binCount: 16384);

            Assert.Equal(3, result.PixelCount);
            Assert.Equal(0, result.Bins[^1]);
            Assert.Equal(1, result.Bins[2]);
            Assert.Equal(1, result.Bins[4]);
            Assert.Equal(1, result.Bins[9]);
            Assert.Equal(5, result.Mean);
            Assert.Equal(Math.Sqrt(26.0 / 3), result.StandardDeviation, 10);
            Assert.Equal(4, result.Median);
        }

        [Fact]
        public void Analyze_ClampsBinsButPreservesFullUshortStatisticsAndExactMedian()
        {
            var result = RawHistogram.Analyze(
                new byte[] { 0xFF, 0xFF, 0x00, 0x40, 0x00, 0x00, 0x01, 0x00 }, binCount: 2);

            Assert.Equal(new[] { 2, 2 }, result.Bins);
            Assert.Equal(20480, result.Mean);
            Assert.Equal(8192.5, result.Median);
            Assert.Equal(Math.Sqrt((65535.0 * 65535 + 16384.0 * 16384 + 1) / 4 - 20480.0 * 20480),
                result.StandardDeviation, 10);
        }

        [Fact]
        public void Analyze_RawAcceptsOneImageSampleAfterFpa()
        {
            var result = RawHistogram.Analyze(new byte[] { 0xFF, 0xFF, 0x07, 0x00 }, true);

            Assert.Equal(1, result.PixelCount);
            Assert.Equal(7, result.Mean);
            Assert.Equal(0, result.StandardDeviation);
            Assert.Equal(7, result.Median);
            Assert.Equal(1, result.Bins[0]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public void Analyze_RawRejectsMissingImageSampleOrOddByteCount(int byteCount)
        {
            Assert.Throws<ArgumentException>(() => RawHistogram.Analyze(new byte[byteCount], true));
        }

        [Fact]
        public void Analyze_DefaultRetainsLegacyOddByteAndEmptyHandling()
        {
            byte[] pixels = { 0x01, 0x00, 0x7F };
            var result = RawHistogram.Analyze(pixels);

            Assert.Equal(RawHistogram.Compute(pixels), result.Bins);
            Assert.Equal(1, result.PixelCount);
            Assert.Equal(1, result.Mean);
            Assert.Equal(1, result.Median);
            Assert.Equal(0, result.StandardDeviation);
            foreach (byte[] empty in new[] { Array.Empty<byte>(), new byte[] { 0x01 } })
            {
                var analysis = RawHistogram.Analyze(empty);
                Assert.Equal(RawHistogram.Compute(empty), analysis.Bins);
                Assert.Equal(0, analysis.PixelCount);
                Assert.Equal(0, analysis.Mean);
                Assert.Equal(0, analysis.StandardDeviation);
                Assert.Equal(0, analysis.Median);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Analyze_RejectsNonPositiveBinCount(int binCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RawHistogram.Analyze(new byte[4], true, binCount));
        }
    }
}
