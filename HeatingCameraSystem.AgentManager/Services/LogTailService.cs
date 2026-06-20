using System;
using System.Collections.Concurrent;
using System.IO;
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

        public LogTailService(INatsCommunicationService nats, ManagerSettings settings,
            ILogger<LogTailService> logger)
        {
            _nats     = nats;
            _settings = settings;
            _logger   = logger;
        }

        public void Watch(string agentId, string logDirectory)
        {
            if (_watchers.ContainsKey(agentId)) return;

            Directory.CreateDirectory(logDirectory);

            var watcher = new FileSystemWatcher(logDirectory, "*.log")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += (_, e) => _ = TailFileAsync(agentId, e.FullPath);
            _watchers[agentId] = watcher;
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

        private async Task TailFileAsync(string agentId, string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long offset = _offsets.GetOrAdd(agentId, 0);
                stream.Seek(offset, SeekOrigin.Begin);

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    ProcessLine(agentId, line);
                }
                _offsets[agentId] = stream.Position;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LogTail read failed for {AgentId}", agentId);
            }
        }

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
                /* malformed NDJSON — skip */
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
        }
    }
}
