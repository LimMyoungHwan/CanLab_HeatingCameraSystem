using System;
using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Persists a thermal capture as the radiometric source of truth: a raw little-endian
    /// 14-bit <c>.y16</c> payload plus a self-describing <c>.json</c> sidecar. The 8-bit
    /// normalized JPG that the console Agent used to write is intentionally NOT the primary
    /// format here (it discards temperature data). PNG preview is written by the caller layer.
    /// </summary>
    public sealed class ThermalCaptureWriter
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly string _rootDir;

        public ThermalCaptureWriter(string rootDir)
        {
            _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
            Directory.CreateDirectory(_rootDir);
        }

        public CaptureFiles Write(ThermalFrame frame, string agentId, int cameraIndex, string? recipeStepId = null)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (frame.Pixels.Length != frame.Width * frame.Height)
            {
                throw new ArgumentException("Frame dimensions do not match pixel data.", nameof(frame));
            }

            (ushort min, ushort max) = MinMax(frame.Pixels);

            string baseName = $"{MakeFileSafe(agentId)}_{frame.Timestamp:yyyyMMdd_HHmmss_fff}";
            string y16Path = Path.Combine(_rootDir, baseName + ".y16");
            string jsonPath = Path.Combine(_rootDir, baseName + ".json");

            var bytes = new byte[frame.Pixels.Length * sizeof(ushort)];
            Buffer.BlockCopy(frame.Pixels, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(y16Path, bytes);

            var meta = new CaptureMetadata
            {
                AgentId = agentId,
                CameraIndex = cameraIndex,
                Width = frame.Width,
                Height = frame.Height,
                PixelFormat = "Y16_14bit_LE",
                TimestampUtc = frame.Timestamp.ToUniversalTime(),
                Min = min,
                Max = max,
                RecipeStepId = recipeStepId,
                Y16File = Path.GetFileName(y16Path)
            };

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(meta, JsonOpts));

            return new CaptureFiles(meta, y16Path, jsonPath, PngPath: null);
        }

        private static (ushort Min, ushort Max) MinMax(ushort[] pixels)
        {
            if (pixels.Length == 0) return (0, 0);

            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            foreach (ushort p in pixels)
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }

            return (min, max);
        }

        private static string MakeFileSafe(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }
    }
}
