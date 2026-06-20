using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentManager.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeatingCameraSystem.AgentManager.Services
{
    public class LogDumpHandler
    {
        private readonly INatsCommunicationService _nats;
        private readonly ManagerSettings _settings;
        private readonly ILogger<LogDumpHandler> _logger;

        public LogDumpHandler(INatsCommunicationService nats, ManagerSettings settings,
            ILogger<LogDumpHandler> logger)
        {
            _nats     = nats;
            _settings = settings;
            _logger   = logger;
        }

        public void Subscribe()
        {
            _nats.SubscribeLogDumpRequestAsync(_settings.PCId, req => _ = HandleAsync(req));
        }

        private async Task HandleAsync(LogDumpRequestMessage req)
        {
            _logger.LogInformation("LogDump requested for {AgentId}, max={Max}B", req.AgentId, req.MaxBytes);

            var logDir = Path.Combine(_settings.InstallRoot, "logs", req.AgentId);
            if (!Directory.Exists(logDir))
            {
                _logger.LogWarning("LogDump: log dir not found for {AgentId}", req.AgentId);
                return;
            }

            var logFiles = Directory.GetFiles(logDir, "*.log")
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
                    var content = await File.ReadAllBytesAsync(file);
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
