using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 생산 저장 규칙(<c>[AISEN-TI][GEN3][열캘]</c>)의 Agent 측 기록·전송 담당.
    /// <para>
    /// 촬영은 로컬 버퍼에 먼저 쓴다. 네트워크가 순단되면 촬영이 통째로 날아가는데, 열챔버는
    /// 온도 안정화만 수십 분이라 재현 비용이 전송 지연보다 훨씬 크기 때문이다.
    /// </para>
    /// <para>
    /// 폴더가 완성된 뒤에만 <c>robocopy /MOVE</c>로 Master에 밀어넣는다. 파일 단위로 옮기면
    /// 후처리 툴이 복사 도중인 파일을 읽을 수 있고, <c>/MOVE</c>가 성공한 파일만 지우므로
    /// 실패분은 로컬에 남아 다음 시도에서 이어진다.
    /// </para>
    /// </summary>
    public sealed class ProductionCaptureSink
    {
        private const string UnknownSerial = "UNKNOWN";

        private readonly string _bufferRoot;
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        public ProductionCaptureSink(string bufferRoot)
        {
            _bufferRoot = bufferRoot ?? throw new ArgumentNullException(nameof(bufferRoot));
        }

        public static bool IsEnabled(CaptureCommandMessage cmd)
            => !string.IsNullOrWhiteSpace(cmd.StorageRootUnc)
               && !string.IsNullOrWhiteSpace(cmd.ConditionFolder)
               && !string.IsNullOrWhiteSpace(cmd.BlackBodyFolder)
               && !string.IsNullOrWhiteSpace(cmd.FilePrefix);

        /// <summary>
        /// 센서번호는 Master가 모르므로(<c>CameraDevice</c>에 필드가 없다) Agent가 여기서 붙인다.
        /// </summary>
        public static string ConditionRelativeDirectory(CameraDescriptor cam, CaptureCommandMessage cmd)
        {
            string serial = string.IsNullOrWhiteSpace(cam.CameraSerialNumber) ? UnknownSerial : cam.CameraSerialNumber!;
            string sensorFolder = string.IsNullOrWhiteSpace(cmd.ProductNumber)
                ? serial
                : $"{serial}_{cmd.ProductNumber}";

            return Path.Combine(sensorFolder, cmd.ConditionFolder);
        }

        public static string RelativeDirectory(CameraDescriptor cam, CaptureCommandMessage cmd)
            => Path.Combine(ConditionRelativeDirectory(cam, cmd), cmd.BlackBodyFolder);

        public string WriteShot(CameraDescriptor cam, CaptureCommandMessage cmd, ThermalFrame rawFrame, short? fpaRaw, int shotIndex)
        {
            string directory = Path.Combine(_bufferRoot, RelativeDirectory(cam, cmd));
            return RawCaptureWriter.Write(directory, CaptureNamingRule.FileName(cmd.FilePrefix, shotIndex), rawFrame, fpaRaw);
        }

        /// <summary>아직 Master로 옮기지 못한 로컬 <c>.raw</c> 파일 수.</summary>
        public int PendingFiles
        {
            get
            {
                try
                {
                    return Directory.Exists(_bufferRoot)
                        ? Directory.EnumerateFiles(_bufferRoot, "*.raw", SearchOption.AllDirectories).Count()
                        : 0;
                }
                catch (IOException)
                {
                    return 0;
                }
            }
        }

        /// <summary>bias.json은 흑체 폴더가 아니라 그 부모(조건 폴더)에 놓인다(PDF 3.1 · main.py:1064).</summary>
        public string WriteBiasJson(CameraDescriptor cam, CaptureCommandMessage cmd, string biasJson)
        {
            string directory = Path.Combine(_bufferRoot, ConditionRelativeDirectory(cam, cmd));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "bias.json");
            File.WriteAllText(path, biasJson);
            return path;
        }

        public async Task SyncFolderAsync(CameraDescriptor cam, CaptureCommandMessage cmd, CancellationToken ct = default)
        {
            string relative = RelativeDirectory(cam, cmd);
            await SyncAsync(
                Path.Combine(_bufferRoot, relative),
                Path.Combine(cmd.StorageRootUnc, relative),
                "*.raw *.json",
                ct).ConfigureAwait(false);

            // 조건 폴더는 형제 흑체 폴더가 아직 촬영 중일 수 있으므로 bias.json만 따로 올린다.
            string conditionRelative = ConditionRelativeDirectory(cam, cmd);
            await SyncAsync(
                Path.Combine(_bufferRoot, conditionRelative),
                Path.Combine(cmd.StorageRootUnc, conditionRelative),
                "bias.json",
                ct).ConfigureAwait(false);
        }

        private async Task SyncAsync(string source, string destination, string filePatterns, CancellationToken ct)
        {
            if (!Directory.Exists(source)) return;

            try { await _syncGate.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                using var process = Process.Start(new ProcessStartInfo("robocopy")
                {
                    // /MOVE = 성공한 파일만 원본에서 지운다 → 실패분이 로컬에 남아 재시도가 이어진다.
                    Arguments = $"\"{source}\" \"{destination}\" {filePatterns} /MOVE /R:3 /W:5 /NJH /NJS /NP /NFL /NDL",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null) return;
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                // robocopy는 8 미만을 성공으로 정의한다(0=변경 없음, 1=복사됨, 2=추가 항목 …).
                if (process.ExitCode >= 8)
                {
                    Debug.WriteLine($"[ProductionSink] robocopy failed ({process.ExitCode}): {source} -> {destination}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProductionSink] sync failed {source} -> {destination}: {ex.Message}");
            }
            finally
            {
                _syncGate.Release();
            }
        }
    }
}
