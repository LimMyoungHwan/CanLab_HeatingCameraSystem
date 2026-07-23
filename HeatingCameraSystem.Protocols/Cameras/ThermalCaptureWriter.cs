using System;
using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Models;
using OpenCvSharp;

namespace HeatingCameraSystem.Protocols.Cameras
{
    public sealed class ThermalCaptureWriter
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly string _rootDir;
        private readonly CaptureImageFormat _format;

        public ThermalCaptureWriter(string rootDir, CaptureImageFormat format = CaptureImageFormat.Y16Raw)
        {
            _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
            _format = format;
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

            if (_format == CaptureImageFormat.Tiff16)
            {
                WriteTiff16(Path.Combine(_rootDir, baseName + ".tif"), frame);
            }

            return new CaptureFiles(meta, y16Path, jsonPath, PngPath: null);
        }

        private static void WriteTiff16(string path, ThermalFrame frame)
        {
            using var mat = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_16UC1, frame.Pixels);
            mat.SaveImage(path);
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
