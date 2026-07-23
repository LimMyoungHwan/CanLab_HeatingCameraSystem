using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Turns a 14-bit Y16 <see cref="ThermalFrame"/> into a BGR24 byte buffer via plateau
    /// histogram equalization (thermal AGC, ported from the reference Python
    /// two_point_viewer.thresh_plateau_hist_eq) followed by an iron palette LUT. Single source of
    /// the live thermal look — shared by the AgentUI preview and the NATS color-JPEG encoder so the
    /// two never drift.
    /// </summary>
    public static class ThermalColorizer
    {
        private const int Bins = 1 << 14;

        // ponytail: AGC plateau (per-bin count cap). 100 = Python parity. The display-contrast knob —
        // raise if flat scenes wash out, lower if noise is over-amplified.
        private const int PlateauLimit = 100;

        private static readonly uint[] IronLut = BuildIronLut();

        /// <summary>Returns a BGR24 buffer (stride = <c>Width * 3</c>) for the frame.</summary>
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

            // Plateau-clipped cumulative histogram (the AGC transfer function).
            var cdf = new long[Bins];
            long cum = 0;
            for (int i = 0; i < Bins; i++)
            {
                int c = hist[i];
                cum += c > PlateauLimit ? PlateauLimit : c;
                cdf[i] = cum;
            }

            // Normalize the CDF over its populated range (leading zero bins stay black),
            // producing a 14-bit -> 8-bit grayscale LUT.
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

        // Classic ironbow palette: black -> purple -> magenta -> red -> orange -> amber -> white,
        // built by linearly interpolating anchor colors into a 256-entry 0x00RRGGBB LUT.
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
