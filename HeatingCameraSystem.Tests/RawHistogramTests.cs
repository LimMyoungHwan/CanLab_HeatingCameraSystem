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
    }
}
