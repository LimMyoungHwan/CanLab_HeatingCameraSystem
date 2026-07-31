using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeatingCameraSystem.AgentUI.Services
{
    /// <summary>
    /// Runs shutdown steps sequentially on the thread pool with a total timeout, so a step that
    /// hangs on a stuck/removed serial port (shutter close, <c>SerialPort.Dispose</c>) cannot block
    /// the WPF UI thread in <c>App.OnExit</c> forever. Best-effort: per-step exceptions are swallowed.
    /// </summary>
    internal static class AppShutdown
    {
        /// <summary>
        /// Runs each step in order off the calling thread. Returns true if all steps finished
        /// within <paramref name="totalTimeout"/>, false if the budget was exceeded (a hung step
        /// does not block past the timeout — the process can then exit and release the port).
        /// </summary>
        public static bool Run(IEnumerable<Func<Task>> steps, TimeSpan totalTimeout)
        {
            var work = Task.Run(async () =>
            {
                foreach (Func<Task> step in steps)
                {
                    try { await step().ConfigureAwait(false); }
                    catch { /* best-effort shutdown */ }
                }
            });
            return work.Wait(totalTimeout);
        }
    }
}
