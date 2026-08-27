using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;

namespace HeatingCameraSystem.Simulator.Cameras;

/// <summary>캡처 출력 경로에 쓰기 실패했을 때 원인 예외를 감싸 던진다.</summary>
public sealed class SyntheticCaptureStoreException : Exception
{
    public SyntheticCaptureStoreException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 합성 캡처를 디스크에 저장하는 담당. 실 Agent의 ImageStorage 구조를 흉내 내
/// <c>camera_{index}</c> 하위 폴더에 JPEG로 남기므로, E2E 드라이버가 결과 메시지의
/// 파일 경로 존재 여부를 실 운용과 같은 방식으로 검증할 수 있다.
/// </summary>
public sealed class SyntheticCaptureStore
{
    private readonly string _outputPath;

    public SyntheticCaptureStore(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must be non-empty.", nameof(outputPath));

        _outputPath = outputPath;
    }

    /// <summary>프레임을 JPEG로 인코딩해 저장하고 절대 경로와 바이트를 함께 돌려준다.</summary>
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
