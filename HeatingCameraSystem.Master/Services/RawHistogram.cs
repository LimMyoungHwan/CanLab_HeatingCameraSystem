using System;

namespace HeatingCameraSystem.Master.Services
{
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
    }
}
