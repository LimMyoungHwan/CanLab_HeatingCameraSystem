using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// 14비트 Y16 <see cref="ThermalFrame"/>을 라이브 표시용 frozen false-color 열영상
    /// <see cref="BitmapSource"/>로 변환한다. 두 단계다: (1) plateau 히스토그램 평활화(열영상 AGC,
    /// 레퍼런스 Python two_point_viewer.thresh_plateau_hist_eq 이식)가 bin별 클리핑으로
    /// 14비트 → 8비트를 매핑해 평탄한 배경/데드픽셀이 대비를 뭉개지 못하게 하고,
    /// (2) iron 팔레트 LUT가 8비트 → 컬러를 매핑한다. 카메라 루프 스레드에서 만들어 UI 스레드에서
    /// 할당할 수 있도록 Freeze한다. 미리보기 전용 — 14비트 방사 측정 데이터는 보존되지 않는다
    /// (원본은 따로 저장).
    /// </summary>
    public static class ThermalFrameBitmapSourceConverter
    {
        private const int Bins = 1 << 14;      // 14비트 열영상 범위 (0..16383)

        // ponytail: 열영상 AGC plateau(bin별 카운트 상한). 100 = Python 동일값. 표시 대비의 튜닝
        // 노브다 — 낮추면 평탄해지고 높이면 거칠어진다. 대비가 이상하면 조정할 것.
        private const int PlateauLimit = 100;

        private static readonly uint[] IronLut = BuildIronLut();

        public static BitmapSource ToBitmapSource(ThermalFrame f)
        {
            if (f.Width <= 0 || f.Height <= 0 || f.Pixels.Length != f.Width * f.Height)
            {
                throw new ArgumentException("Thermal frame dimensions do not match pixel data.", nameof(f));
            }

            ushort[] px = f.Pixels;

            var hist = new int[Bins];
            for (int i = 0; i < px.Length; i++)
            {
                hist[px[i] & 0x3FFF]++;
            }

            // plateau로 클리핑한 누적 히스토그램(AGC 전달 함수).
            var cdf = new long[Bins];
            long cum = 0;
            for (int i = 0; i < Bins; i++)
            {
                int c = hist[i];
                cum += c > PlateauLimit ? PlateauLimit : c;
                cdf[i] = cum;
            }

            // 값이 채워진 구간 기준으로 CDF를 정규화한다(앞쪽의 0 bin은 검정 유지).
            // 결과는 14비트 → 8비트 그레이스케일 LUT다.
            long cdfLast = cdf[Bins - 1];
            long cdfMin = 0;
            for (int i = 0; i < Bins; i++)
            {
                if (cdf[i] != 0) { cdfMin = cdf[i]; break; }
            }

            var grayLut = new byte[Bins];
            double denom = cdfLast - cdfMin;
            if (denom > 0)
            {
                for (int i = 0; i < Bins; i++)
                {
                    if (cdf[i] == 0) continue;
                    int v = (int)((cdf[i] - cdfMin) / denom * 255.0);
                    grayLut[i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                }
            }

            // 14비트 → 그레이 → iron 컬러, Bgr24로 패킹.
            int w = f.Width, h = f.Height;
            int stride = w * 3;
            var bytes = new byte[stride * h];
            for (int i = 0, j = 0; i < px.Length; i++, j += 3)
            {
                uint c = IronLut[grayLut[px[i] & 0x3FFF]];
                bytes[j]     = (byte)(c & 0xFF);          // B
                bytes[j + 1] = (byte)((c >> 8) & 0xFF);   // G
                bytes[j + 2] = (byte)((c >> 16) & 0xFF);  // R
            }

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), bytes, stride, 0);
            bmp.Freeze();
            return bmp;
        }

        // 전형적인 ironbow 팔레트: 검정 → 보라 → 마젠타 → 빨강 → 주황 → 호박 → 흰색.
        // 앵커 색을 선형 보간해 256 엔트리 0x00RRGGBB LUT를 만든다.
        private static uint[] BuildIronLut()
        {
            (int Pos, int R, int G, int B)[] anchors =
            {
                (0,     0,   0,   0),
                (32,    20,  0,   70),
                (64,    70,  0,   110),
                (96,    130, 10,  110),
                (128,   175, 30,  85),
                (160,   210, 60,  40),
                (192,   235, 110, 10),
                (224,   250, 175, 0),
                (248,   253, 225, 80),
                (255,   255, 255, 255),
            };

            var lut = new uint[256];
            for (int a = 0; a < anchors.Length - 1; a++)
            {
                var (p0, r0, g0, b0) = anchors[a];
                var (p1, r1, g1, b1) = anchors[a + 1];
                int span = p1 - p0;
                for (int p = p0; p <= p1; p++)
                {
                    double t = span == 0 ? 0.0 : (double)(p - p0) / span;
                    uint r = (uint)(r0 + (r1 - r0) * t);
                    uint g = (uint)(g0 + (g1 - g0) * t);
                    uint b = (uint)(b0 + (b1 - b0) * t);
                    lut[p] = (r << 16) | (g << 8) | b;
                }
            }

            return lut;
        }
    }
}
