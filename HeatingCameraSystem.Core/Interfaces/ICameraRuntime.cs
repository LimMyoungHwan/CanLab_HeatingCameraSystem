using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// Owns exactly one physical camera: one handle, one continuous Y16 read loop.
    /// Live view subscribes to <see cref="FrameReady"/>; NATS/recipe capture snapshots the
    /// latest frame via <see cref="CaptureSnapshotAsync"/> (tee — never opens a second
    /// handle, never pauses live view). Multiple runtimes (distinct indices) can coexist
    /// in one process without contention.
    /// </summary>
    public interface ICameraRuntime : IDisposable
    {
        int CameraIndex { get; }
        CameraRuntimeStatus Status { get; }
        bool IsRunning { get; }

        /// <summary>Most recent frame, or null before the first frame. Updated atomically.</summary>
        ThermalFrame? LatestFrame { get; }

        event EventHandler<ThermalFrame>? FrameReady;
        event EventHandler<CameraRuntimeStatus>? StatusChanged;

        Task StartAsync(CancellationToken ct = default);
        Task StopAsync();

        /// <summary>
        /// Returns a capture frame teed from the live loop. If <paramref name="maxAge"/> is
        /// null the current latest frame is returned immediately; otherwise, if the latest
        /// frame is older than maxAge (or none exists), waits up to
        /// <paramref name="nextFrameTimeout"/> for a fresh frame. Returns null if the runtime
        /// is not producing frames (never started, or stalled past the timeout with no frame).
        /// </summary>
        Task<ThermalFrame?> CaptureSnapshotAsync(
            TimeSpan? maxAge = null,
            TimeSpan? nextFrameTimeout = null,
            CancellationToken ct = default);
    }
}
