using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using HeatingCameraSystem.AgentManager.Config;

namespace HeatingCameraSystem.AgentManager.Services
{
    internal static class AgentLogFiles
    {
        internal static bool IsTrustedAgentUiDirectory([NotNullWhen(true)] string? directory, ManagerSettings settings)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return false;
            try
            {
                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                if (!string.IsNullOrWhiteSpace(settings.AgentUiLogDirectory))
                    return SamePath(path, settings.AgentUiLogDirectory);
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HeatingCameraSystem", "AgentUI", "logs");
                if (SamePath(path, local)) return true;
                // Manager는 서비스 계정, AgentUI는 대화형 사용자로 실행된다. 임의 NATS 경로 대신
                // Windows 표준 사용자 프로필의 정확한 AgentUI 로그 폴더만 허용한다.
                string? drive = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrWhiteSpace(drive)) return false;
                string profiles = Path.Combine(drive, "Users");
                string relative = Path.GetRelativePath(profiles, path);
                string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                return parts.Length == 6 && parts[0] != ".."
                    && string.Equals(Path.Combine(parts.Skip(1).ToArray()),
                        Path.Combine("AppData", "Local", "HeatingCameraSystem", "AgentUI", "logs"), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                return false;
            }
        }

        internal static string ResolveDirectory(ManagerSettings settings, string agentId, string? reportedDirectory)
        {
            if (IsTrustedAgentUiDirectory(reportedDirectory, settings)) return Path.GetFullPath(reportedDirectory);
            if (string.IsNullOrWhiteSpace(agentId) || agentId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || agentId is "." or ".." || agentId.Contains(Path.DirectorySeparatorChar) || agentId.Contains(Path.AltDirectorySeparatorChar))
                throw new ArgumentException("Invalid log AgentId", nameof(agentId));
            return Path.Combine(settings.InstallRoot, "logs", agentId);
        }

        internal static IEnumerable<string> Enumerate(string directory) => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).Where(path =>
                path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
            : Array.Empty<string>();

        internal static bool BelongsToAgent(string line, string agentId)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.TryGetProperty("AgentId", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                    && string.Equals(id.GetString(), agentId, StringComparison.Ordinal);
            }
            catch (JsonException) { return false; }
        }

        private static bool SamePath(string left, string right) => string.Equals(left,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    }
}
