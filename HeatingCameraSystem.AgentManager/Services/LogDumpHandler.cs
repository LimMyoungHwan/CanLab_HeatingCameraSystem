using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.State;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    /// <summary>
    /// <c>server.req.log.{PCId}</c>의 로그 덤프 요청을 받아 해당 Agent의 로그 파일들을 모아
    /// gzip 압축한 뒤 <c>agent-mgr.log.dump.{PCId}</c>로 응답한다.
    /// </summary>
    public class LogDumpHandler : IDisposable
    {
        private bool _disposed;
        private readonly CancellationTokenSource _lifetime = new();
        private IAsyncDisposable? _subscription;
        private readonly ManagerStateStore? _store;
        private readonly INatsCommunicationService _nats;
        private readonly ManagerSettings _settings;
        private readonly ILogger<LogDumpHandler> _logger;

        public LogDumpHandler(INatsCommunicationService nats, ManagerSettings settings,
            ILogger<LogDumpHandler> logger, ManagerStateStore? store = null)
        {
            _nats     = nats;
            _settings = settings;
            _logger   = logger;
            _store = store;
        }

        /// <summary><c>server.req.log.{PCId}</c> 구독을 시작한다.</summary>
        public void Subscribe() => _ = SubscribeAsync();

        public async Task SubscribeAsync()
        {
            _subscription = await _nats.SubscribeLogDumpRequestScopedAsync(_settings.PCId, req => _ = HandleAsync(req), _lifetime.Token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (_subscription is not null) _ = _subscription.DisposeAsync();
            _lifetime.Dispose();
        }

        /// <summary>
        /// 로그 파일을 이름 내림차순(최신 우선)으로 이어 붙이되, MaxBytes를 넘게 만드는 파일은
        /// 뒷부분만 잘라 넣고 중단하며 IsTruncated를 참으로 둔다. 결과는 gzip 압축해 발행한다.
        /// </summary>
        internal async Task HandleAsync(LogDumpRequestMessage req)
        {
            _logger.LogInformation("LogDump requested for {AgentId}, max={Max}B", req.AgentId, req.MaxBytes);

            CameraEntry? entry = _store?.GetByAgentId(req.AgentId);
            string logDir;
            try { logDir = AgentLogFiles.ResolveDirectory(_settings, req.AgentId, entry?.AgentLogDirectory); }
            catch (ArgumentException ex) { _logger.LogWarning(ex, "Invalid log dump target"); return; }
            bool filter = AgentLogFiles.IsTrustedAgentUiDirectory(entry?.AgentLogDirectory, _settings);
            if (req.MaxBytes <= 0) return;
            if (!Directory.Exists(logDir))
            {
                _logger.LogWarning("LogDump: log dir not found for {AgentId}", req.AgentId);
                return;
            }

            var logFiles = AgentLogFiles.Enumerate(logDir)
                .OrderByDescending(f => f)
                .ToArray();

            if (logFiles.Length == 0) return;

            byte[] rawBytes;
            bool isTruncated = false;

            using (var ms = new MemoryStream())
            {
                long written = 0;
                foreach (var file in logFiles)
                {
                    byte[] content;
                    if (filter)
                    {
                        string[] lines = await File.ReadAllLinesAsync(file);
                        content = Encoding.UTF8.GetBytes(string.Join("\n", lines.Where(line => AgentLogFiles.BelongsToAgent(line, req.AgentId))) + "\n");
                    }
                    else content = await File.ReadAllBytesAsync(file);
                    if (written + content.Length > req.MaxBytes)
                    {
                        int remaining = req.MaxBytes - (int)written;
                        if (remaining > 0)
                        {
                            await ms.WriteAsync(content, content.Length - remaining, remaining);
                        }
                        isTruncated = true;
                        break;
                    }
                    await ms.WriteAsync(content);
                    written += content.Length;
                }
                rawBytes = ms.ToArray();
            }

            byte[] gzipBytes;
            using (var outMs = new MemoryStream())
            {
                using (var gz = new GZipStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
                    await gz.WriteAsync(rawBytes);
                gzipBytes = outMs.ToArray();
            }

            await _nats.PublishLogDumpAsync(new LogDumpMessage
            {
                PCId          = _settings.PCId,
                AgentId       = req.AgentId,
                GzipBytes     = gzipBytes,
                OriginalBytes = rawBytes.Length,
                IsTruncated   = isTruncated,
                Timestamp     = DateTime.UtcNow,
            });

            _logger.LogInformation("LogDump sent for {AgentId}: {Orig}B → {Gz}B gzip",
                req.AgentId, rawBytes.Length, gzipBytes.Length);
        }
    }
}
