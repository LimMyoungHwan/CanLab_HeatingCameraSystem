using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Per-camera flat-field correction captured with the shutter closed: a one-point (offset)
    /// non-uniformity map removes the fixed-pattern shading/gradient, and outlier ("dead") pixels
    /// detected against their local neighborhood are replaced with the neighbor average. Gain
    /// non-uniformity needs a two-point (cold/hot blackbody) calibration and is out of scope here.
    /// Maps are swapped atomically, so <see cref="Apply"/> on the frame threads never tears against
    /// a capture on another thread.
    /// </summary>
    public sealed class ThermalNucCorrector
    {
        private const int Mask = 0x3FFF;

        private volatile int[]? _offset;
        private volatile int[]? _deadPixels;
        private volatile int[][]? _deadNeighbors;

        public bool HasReference => _offset is not null;

        public int DeadPixelCount => _deadPixels?.Length ?? 0;

        public void Clear()
        {
            _offset = null;
            _deadPixels = null;
            _deadNeighbors = null;
        }

        /// <summary>
        /// Builds the offset map and the dead-pixel map from an averaged flat-field frame
        /// (shutter closed).
        /// </summary>
        public void CaptureFromFlat(ThermalFrame flat)
        {
            if (flat is null) throw new ArgumentNullException(nameof(flat));

            ushort[] px = flat.Pixels;
            if (px.Length == 0) { Clear(); return; }

            long sum = 0;
            for (int i = 0; i < px.Length; i++) sum += px[i] & Mask;
            int mean = (int)(sum / px.Length);

            var offset = new int[px.Length];
            for (int i = 0; i < px.Length; i++) offset[i] = (px[i] & Mask) - mean;
            _offset = offset;

            DetectDeadPixels(px, flat.Width, flat.Height);
        }

        /// <summary>Returns a corrected frame, or the input unchanged when no reference is set.</summary>
        public ThermalFrame Apply(ThermalFrame frame)
        {
            int[]? offset = _offset;
            if (offset is null || frame.Pixels.Length != offset.Length) return frame;

            ushort[] src = frame.Pixels;
            var outPx = new ushort[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                int v = (src[i] & Mask) - offset[i];
                outPx[i] = (ushort)(v < 0 ? 0 : v > Mask ? Mask : v);
            }

            int[]? dead = _deadPixels;
            int[][]? neighbors = _deadNeighbors;
            if (dead is not null && neighbors is not null && dead.Length == neighbors.Length)
            {
                for (int d = 0; d < dead.Length; d++)
                {
                    int[] nb = neighbors[d];
                    if (nb.Length == 0) continue;
                    int acc = 0;
                    for (int k = 0; k < nb.Length; k++) acc += outPx[nb[k]];
                    outPx[dead[d]] = (ushort)(acc / nb.Length);
                }
            }

            return new ThermalFrame(outPx, frame.Width, frame.Height, frame.Timestamp);
        }

        // Flags pixels that deviate from their 5x5 local median far beyond the typical noise spread
        // (robust to the FPN gradient), then precomputes each one's good neighbors so the per-frame
        // correction is a cheap neighbor average. Runs once per capture.
        private void DetectDeadPixels(ushort[] px, int w, int h)
        {
            if (w <= 0 || h <= 0 || px.Length != w * h)
            {
                _deadPixels = null;
                _deadNeighbors = null;
                return;
            }

            var dev = new int[px.Length];
            var window = new int[25];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int n = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            window[n++] = px[yy * w + xx] & Mask;
                        }
                    }
                    Array.Sort(window, 0, n);
                    dev[y * w + x] = (px[y * w + x] & Mask) - window[n / 2];
                }
            }

            double meanDev = 0;
            for (int i = 0; i < dev.Length; i++) meanDev += dev[i];
            meanDev /= dev.Length;
            double varDev = 0;
            for (int i = 0; i < dev.Length; i++) { double e = dev[i] - meanDev; varDev += e * e; }
            double sd = Math.Sqrt(varDev / dev.Length);
            double threshold = Math.Max(50.0, 5.0 * sd);

            var deadList = new List<int>();
            for (int i = 0; i < dev.Length; i++)
            {
                if (Math.Abs(dev[i]) > threshold) deadList.Add(i);
            }

            if (deadList.Count == 0 || deadList.Count > px.Length / 20)
            {
                _deadNeighbors = Array.Empty<int[]>();
                _deadPixels = Array.Empty<int>();
                return;
            }

            var deadSet = new HashSet<int>(deadList);
            var neighbors = new int[deadList.Count][];
            for (int d = 0; d < deadList.Count; d++)
            {
                int idx = deadList[d];
                int y = idx / w, x = idx % w;
                var nb = new List<int>(8);
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        int nIdx = yy * w + xx;
                        if (!deadSet.Contains(nIdx)) nb.Add(nIdx);
                    }
                }
                neighbors[d] = nb.ToArray();
            }

            _deadNeighbors = neighbors;
            _deadPixels = deadList.ToArray();
        }
    }
}
