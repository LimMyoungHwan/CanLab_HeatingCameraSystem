using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.Simulator.Cameras;

public sealed class SyntheticCaptureStoreException : Exception
{
    public SyntheticCaptureStoreException(string message, Exception inner) : base(message, inner) { }
}

public sealed class SyntheticCaptureStore
{
    private readonly string _outputPath;

    public SyntheticCaptureStore(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must be non-empty.", nameof(outputPath));

        _outputPath = outputPath;
    }

    public (string Path, byte[] Bytes) Persist(int cameraIndex, long sequence, ThermalFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        try
        {
            string cameraDir = System.IO.Path.Combine(_outputPath, $"camera_{cameraIndex}");
            Directory.CreateDirectory(cameraDir);

            byte[] bytes = ThermalPreviewEncoder.EncodeColorJpeg(frame);
            string fileName = $"capture_{cameraIndex}_{sequence}_{frame.Timestamp:yyyyMMdd_HHmmss_fff}.jpg";
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(cameraDir, fileName));
            File.WriteAllBytes(path, bytes);
            return (path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new SyntheticCaptureStoreException($"Synthetic capture output path '{_outputPath}' is not writable.", ex);
        }
    }
}
