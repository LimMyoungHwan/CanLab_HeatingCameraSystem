using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    /// <summary>
    /// 각 Agent의 NDJSON 로그 파일을 tail하여 ERROR/FATAL(+선택 WARN) 라인을
    /// NATS LogAlert로 즉시 전송한다.
    /// </summary>
    public class LogTailService : IDisposable
    {
        private readonly INatsCommunicationService _nats;
        private readonly ManagerSettings _settings;
        private readonly ILogger<LogTailService> _logger;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly ConcurrentDictionary<string, long> _offsets = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileGates = new();

        public LogTailService(INatsCommunicationService nats, ManagerSettings settings,
            ILogger<LogTailService> logger)
        {
            _nats     = nats;
            _settings = settings;
            _logger   = logger;
        }

        /// <summary>AgentUI NDJSON 또는 콘솔 로그를 감시한다. 공유 AgentUI 파일은 반드시 AgentId로 필터링한다.</summary>
        public void Watch(string agentId, string logDirectory, bool filterByAgentId = false)
        {
            if (filterByAgentId && !AgentLogFiles.IsTrustedAgentUiDirectory(logDirectory, _settings)) return;
            if (_watchers.TryGetValue(agentId, out FileSystemWatcher? current))
            {
                if (string.Equals(current.Path, logDirectory, StringComparison.OrdinalIgnoreCase)) return;
                Unwatch(agentId);
            }
            try
            {
                Directory.CreateDirectory(logDirectory);
                var watcher = new FileSystemWatcher(logDirectory, "*")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };
                FileSystemEventHandler changed = (_, e) =>
                {
                    if (e.FullPath.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                        || e.FullPath.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase))
                        _ = TailFileAsync(agentId, e.FullPath, filterByAgentId);
                };
                watcher.Changed += changed;
                watcher.Created += changed;
                _watchers[agentId] = watcher;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "LogTail watch failed for {AgentId}", agentId); }
        }

        public void Unwatch(string agentId)
        {
            if (_watchers.TryRemove(agentId, out var w))
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _offsets.TryRemove(agentId, out _);
        }

        /// <summary>
        /// 파일별 오프셋과 직렬화를 사용하므로 일별 롤링/중복 파일 이벤트로 로그를 누락하거나 중복 발행하지 않는다.
        /// </summary>
        internal async Task TailFileAsync(string agentId, string filePath, bool filterByAgentId = false)
        {
            string key = agentId + "\n" + filePath;
            SemaphoreSlim gate = _fileGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long offset = _offsets.GetOrAdd(key, 0);
                if (offset > stream.Length) offset = 0;
                stream.Seek(offset, SeekOrigin.Begin);

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!filterByAgentId || AgentLogFiles.BelongsToAgent(line, agentId)) ProcessLine(agentId, line);
                }
                _offsets[key] = stream.Position;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LogTail read failed for {AgentId}", agentId);
            }
            finally { gate.Release(); }
        }

        /// <summary>
        /// NDJSON 한 줄(@l 레벨, @mt/@m 메시지)을 파싱해 Error/Fatal(설정 시 Warning 포함)이면
        /// <c>agent-mgr.log.alert.{PCId}</c>로 LogAlert를 발행한다.
        /// </summary>
        private void ProcessLine(string agentId, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string level = root.TryGetProperty("@l", out var lv) ? lv.GetString() ?? "" : "";
                string msg   = root.TryGetProperty("@mt", out var mt) ? mt.GetString() ?? ""
                             : root.TryGetProperty("@m", out var m)  ? m.GetString() ?? "" : line;

                bool shouldAlert = level is "Error" or "Fatal"
                    || (_settings.WarnAlertEnabled && level == "Warning");

                if (!shouldAlert) return;

                var alertLevel = level switch
                {
                    "Warning" => LogAlertLevel.Warning,
                    "Fatal"   => LogAlertLevel.Fatal,
                    _         => LogAlertLevel.Error,
                };

                _ = _nats.PublishLogAlertAsync(new LogAlertMessage
                {
                    PCId      = _settings.PCId,
                    AgentId   = agentId,
                    Level     = alertLevel,
                    Message   = msg,
                    Timestamp = DateTime.UtcNow,
                });
            }
            catch
            {
                /* 손상된 NDJSON — 건너뜀 */
            }
        }

        public void Dispose()
        {
            foreach (var w in _watchers.Values)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
            _offsets.Clear();
        }
    }
}
