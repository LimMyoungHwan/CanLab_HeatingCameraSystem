using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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

        private static bool IsCaptureFile(string path)
            => path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetFileName(path), "bias.json", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// 한 장을 로컬 버퍼에 쓴다. JPEG는 프레임별 min/max 정규화 8비트 그레이스케일이라 열 데이터가 남지 않으므로
        /// FPA 원시값도 싣지 않는다 — 육안 검사·보고서용 재촬영 전용이다.
        /// </summary>
        public string WriteShot(CameraDescriptor cam, CaptureCommandMessage cmd, ThermalFrame rawFrame, short? fpaRaw, int shotIndex)
        {
            string directory = Path.Combine(_bufferRoot, RelativeDirectory(cam, cmd));

            // 방사 측정 데이터가 없어도 JPEG으로 되돌리지 않는다 — Raw 런은 RawCaptureWriter가
            // 거부해 실패로 보고돼야 한다. 조용한 대체는 못 쓰는 8비트를 성공으로 보이게 한다.
            ProductionCaptureFormat format = cmd.SaveFormat;
            string fileName = CaptureNamingRule.FileName(cmd.FilePrefix, shotIndex, format);

            if (format == ProductionCaptureFormat.Jpeg)
            {
                Directory.CreateDirectory(directory);
                string jpegPath = Path.Combine(directory, fileName);
                File.WriteAllBytes(jpegPath, ThermalPreviewEncoder.EncodeJpeg(rawFrame));
                return jpegPath;
            }

            return RawCaptureWriter.Write(directory, fileName, rawFrame, fpaRaw);
        }

        /// <summary>
        /// 아직 Master로 옮기지 못한 로컬 촬영·BIAS 파일 수. bias.json만 남은 전송 실패도
        /// 0으로 읽혀 "완료"로 보고되지 않도록 포함한다.
        /// </summary>
        public int PendingFiles
        {
            get
            {
                try
                {
                    return Directory.Exists(_bufferRoot)
                        ? Directory.EnumerateFiles(_bufferRoot, "*.*", SearchOption.AllDirectories)
                                   .Count(IsCaptureFile)
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

        /// <summary>
        /// 이번 BIAS 결과를 로컬 버퍼에 기록하고 최종 저장 위치의 내용까지 확인한다.
        /// 재시도는 호출자가 맡는다. 예전 bias.json의 존재만으로 성공 처리하지 않으며,
        /// 저장·전송 실패와 취소를 삼키지 않는다.
        /// </summary>
        public async Task<string> WriteAndVerifyBiasJsonAsync(
            CameraDescriptor cam, CaptureCommandMessage cmd, string biasJson, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(cmd.StorageRootUnc))
                throw new InvalidOperationException("BIAS 파일 저장 경로가 없습니다.");
            if (string.IsNullOrWhiteSpace(cmd.ConditionFolder))
                throw new InvalidOperationException("BIAS 파일 조건 폴더가 없습니다.");
            ValidateBiasJson(biasJson);

            string relative = ConditionRelativeDirectory(cam, cmd);
            string localDirectory = Path.Combine(_bufferRoot, relative);
            string destinationDirectory = Path.Combine(cmd.StorageRootUnc, relative);
            string localPath = Path.Combine(localDirectory, "bias.json");
            string destinationPath = Path.Combine(destinationDirectory, "bias.json");
            string temporaryName = $"bias.{Guid.NewGuid():N}.tmp";
            string localTransfer = Path.Combine(localDirectory, temporaryName);
            string destinationTransfer = Path.Combine(destinationDirectory, temporaryName);

            await _syncGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteAtomicallyAsync(localPath, biasJson, ct).ConfigureAwait(false);
                await VerifyBiasContentsAsync(localPath, biasJson, ct).ConfigureAwait(false);

                // 저장 루트가 로컬 버퍼와 같은 구성에서도 검증한 최종 파일을 지우지 않는다.
                if (string.Equals(Path.GetFullPath(localPath), Path.GetFullPath(destinationPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    return destinationPath;

                // 고유 임시 이름으로 전송하므로 예전 파일이나 복사 중인 파일을 성공 결과로
                // 오인하지 않는다. /MOVE는 이 임시 파일만 옮기며 로컬 bias.json은 검증까지 보존한다.
                await File.WriteAllTextAsync(localTransfer, biasJson, new UTF8Encoding(false), ct).ConfigureAwait(false);
                await CopyBiasFileAsync(localDirectory, destinationDirectory, temporaryName, ct).ConfigureAwait(false);
                await VerifyBiasContentsAsync(destinationTransfer, biasJson, ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                File.Move(destinationTransfer, destinationPath, overwrite: true);
                await VerifyBiasContentsAsync(destinationPath, biasJson, ct).ConfigureAwait(false);

                // 최종 위치 검증이 끝난 파일만 버퍼에서 제거한다. 정리 실패는 이미 검증된
                // 최종 파일의 성공 여부를 바꾸지 않으며 PendingFiles에 그대로 드러난다.
                TryDeleteBiasTemporaryFile(localPath);
                return destinationPath;
            }
            finally
            {
                TryDeleteBiasTemporaryFile(localTransfer);
                TryDeleteBiasTemporaryFile(destinationTransfer);
                _syncGate.Release();
            }
        }

        private static void ValidateBiasJson(string biasJson)
        {
            if (string.IsNullOrWhiteSpace(biasJson))
                throw new InvalidDataException("이번 BIAS 보정 결과가 없어 bias.json을 저장할 수 없습니다.");

            try
            {
                using JsonDocument document = JsonDocument.Parse(biasJson);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !IsHexRegister(root, "CINT", byte.MaxValue)
                    || !IsHexRegister(root, "TINT", ushort.MaxValue)
                    || !IsHexRegister(root, "GSK_LSB", byte.MaxValue)
                    || !IsHexRegister(root, "GFID", byte.MaxValue)
                    || !root.TryGetProperty("GSK_MSB", out JsonElement gskMsb)
                    || gskMsb.ValueKind != JsonValueKind.Number
                    || !gskMsb.TryGetInt32(out int msb)
                    || msb < 0 || msb > byte.MaxValue)
                    throw new InvalidDataException("bias.json의 필수 레지스터 값이 없거나 형식이 잘못되었습니다.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("BIAS 보정 결과가 올바른 JSON 형식이 아닙니다.", ex);
            }
        }

        private static bool IsHexRegister(JsonElement root, string name, int maximum)
            => root.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0 && parsed <= maximum;

        private static async Task WriteAtomicallyAsync(string path, string contents, CancellationToken ct)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException($"BIAS 파일 디렉터리를 확인할 수 없습니다: {path}");

            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, $"bias.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false), ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                TryDeleteBiasTemporaryFile(temporary);
            }
        }

        private static async Task VerifyBiasContentsAsync(string path, string expected, CancellationToken ct)
        {
            string actual = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new IOException($"BIAS 파일 내용이 이번 보정 결과와 다릅니다: {path}");
        }

        private static async Task CopyBiasFileAsync(string source, string destination, string fileName, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destination);
            var startInfo = new ProcessStartInfo("robocopy")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in new[] { source, destination, fileName, "/MOVE", "/R:0", "/W:0", "/NJH", "/NJS", "/NP", "/NFL", "/NDL" })
                startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo)
                ?? throw new IOException($"BIAS 파일 전송을 시작하지 못했습니다: {destination}");
            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // WaitForExitAsync의 취소만으로는 robocopy가 멈추지 않는다. 다음 시도가
                // 이전 전송과 겹치지 않게 프로세스 종료까지 확인한 뒤 실패를 전달한다.
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { /* 종료 확인과 Kill 사이에 이미 끝난 경우. */ }
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }

            ct.ThrowIfCancellationRequested();
            if (process.ExitCode >= 8)
                throw new IOException($"BIAS 파일 전송 실패(robocopy {process.ExitCode}): {source} -> {destination}");
        }

        private static void TryDeleteBiasTemporaryFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException ex) { Debug.WriteLine($"[ProductionSink] BIAS 파일 정리 실패 {path}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Debug.WriteLine($"[ProductionSink] BIAS 파일 정리 실패 {path}: {ex.Message}"); }
        }

        public async Task SyncFolderAsync(CameraDescriptor cam, CaptureCommandMessage cmd, CancellationToken ct = default)
        {
            string relative = RelativeDirectory(cam, cmd);
            await SyncAsync(
                Path.Combine(_bufferRoot, relative),
                Path.Combine(cmd.StorageRootUnc, relative),
                "*.raw *.jpg *.json",
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
