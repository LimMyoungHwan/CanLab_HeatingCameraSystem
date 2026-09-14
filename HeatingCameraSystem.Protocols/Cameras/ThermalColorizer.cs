using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 14비트 Y16 <see cref="ThermalFrame"/>을 plateau 히스토그램 평활화(열화상 AGC, 레퍼런스
    /// Python two_point_viewer.thresh_plateau_hist_eq 포팅)와 iron 팔레트 LUT를 거쳐 BGR24 바이트
    /// 버퍼로 만든다. 라이브 열화상 룩의 단일 소스 — AgentUI 미리보기와 NATS 컬러 JPEG 인코더가
    /// 공유하므로 둘의 표시가 서로 어긋나지 않는다.
    /// </summary>
    public static class ThermalColorizer
    {
        private const int Bins = 1 << 14;

        // ponytail: AGC plateau(빈당 카운트 상한). 100 = Python 파리티. 표시 대비 조절 노브 —
        // 평탄한 장면이 씻겨 보이면 올리고, 노이즈가 과증폭되면 내린다.
        private const int PlateauLimit = 100;

        private static readonly uint[] IronLut = BuildIronLut();

        /// <summary>
        /// gray8 버퍼(256 레벨)에 iron LUT를 적용해 BGR24 버퍼를 반환한다.
        /// 라이브 컬러 모드에서 Master가 수신한 min/max 그레이 JPEG를 iron으로 재착색할 때 쓴다.
        /// </summary>
        public static byte[] Gray8ToBgr24(byte[] gray8)
        {
            var bgr = new byte[gray8.Length * 3];
            for (int i = 0, j = 0; i < gray8.Length; i++, j += 3)
            {
                uint c = IronLut[gray8[i]];
                bgr[j]     = (byte)(c & 0xFF);
                bgr[j + 1] = (byte)((c >> 8) & 0xFF);
                bgr[j + 2] = (byte)((c >> 16) & 0xFF);
            }
            return bgr;
        }

        /// <summary>프레임을 BGR24 버퍼(stride = <c>Width * 3</c>)로 변환해 반환한다.</summary>
        public static byte[] ToBgr24(ThermalFrame f)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
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

            // plateau로 잘라낸 누적 히스토그램(AGC 전달 함수).
            var cdf = new long[Bins];
            long cum = 0;
            for (int i = 0; i < Bins; i++)
            {
                int c = hist[i];
                cum += c > PlateauLimit ? PlateauLimit : c;
                cdf[i] = cum;
            }

            // CDF를 값이 존재하는 구간 기준으로 정규화해(선행 0 빈은 검정 유지)
            // 14비트 → 8비트 그레이스케일 LUT를 만든다.
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

            int stride = f.Width * 3;
            var bgr = new byte[stride * f.Height];
            for (int i = 0, j = 0; i < px.Length; i++, j += 3)
            {
                uint c = IronLut[grayLut[px[i] & 0x3FFF]];
                bgr[j]     = (byte)(c & 0xFF);          // B
                bgr[j + 1] = (byte)((c >> 8) & 0xFF);   // G
                bgr[j + 2] = (byte)((c >> 16) & 0xFF);  // R
            }

            return bgr;
        }

        // 고전 ironbow 팔레트: black → purple → magenta → red → orange → amber → white.
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
