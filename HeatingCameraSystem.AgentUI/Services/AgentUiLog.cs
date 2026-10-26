using System;
using System.IO;
using Serilog;
using Serilog.Formatting.Compact;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// AgentUI 프로세스 전역 Serilog 로거. 인앱 로그 뷰어가 <c>NdjsonLogReader</c>로 되읽는
    /// compact-JSON(CLEF) <c>.ndjson</c>을 <see cref="LogDir"/> 아래 일 단위 롤링 파일로 쓰고,
    /// 디버거 연결 시 읽기 좋은 콘솔 라인도 함께 남긴다.
    /// <see cref="Initialize"/>는 멱등이며 <see cref="CloseAndFlush"/>는 종료 시 호출된다.
    /// </summary>
    public static class AgentUiLog
    {
        private static readonly object Gate = new();
        private static bool _initialized;

        /// <summary>롤링 <c>agentui-*.ndjson</c> 파일이 쌓이는 디렉터리.</summary>
        public static string LogDir => Path.Combine(AgentUiConfig.ConfigDir, "logs");

        /// <summary>활성 로거(<see cref="Initialize"/> 전에는 Serilog의 no-op 로거).</summary>
        public static ILogger Logger => Log.Logger;

        public static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                Directory.CreateDirectory(LogDir);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        new RenderedCompactJsonFormatter(),
                        Path.Combine(LogDir, "agentui-.ndjson"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true)
                    .WriteTo.Console()
                    .CreateLogger();

                _initialized = true;
            }
        }

        /// <summary>원격 BIAS 진단도 카메라별 AgentId 속성을 기록해 Manager가 다른 카메라 로그를 섞지 않게 한다.</summary>
        public static void WriteBiasDiagnostic(string message)
        {
            ILogger logger = Logger;
            int start = message.IndexOf("agent=", StringComparison.Ordinal);
            if (start >= 0)
            {
                start += "agent=".Length;
                int end = message.IndexOf(' ', start);
                string agentId = end < 0 ? message.Substring(start) : message.Substring(start, end - start);
                if (!string.IsNullOrWhiteSpace(agentId)) logger = logger.ForContext("AgentId", agentId);
            }
            if (message.StartsWith("[BIAS] NG", StringComparison.Ordinal) || message.StartsWith("[CAPTURE] NG", StringComparison.Ordinal))
                logger.Error("{BiasDiagnostic}", message);
            else if (message.StartsWith("[BIAS] FAIL", StringComparison.Ordinal))
                logger.Warning("{BiasDiagnostic}", message);
            else logger.Information("{BiasDiagnostic}", message);
        }

        public static void CloseAndFlush()
        {
            lock (Gate)
            {
                Log.CloseAndFlush();
                _initialized = false;
            }
        }
    }
}
