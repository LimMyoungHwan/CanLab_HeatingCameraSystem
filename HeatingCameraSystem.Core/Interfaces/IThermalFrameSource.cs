using System;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// Low-level per-camera thermal frame acquisition. One source owns one physical
    /// camera handle. <see cref="ICameraRuntime"/> wraps a source with the continuous
    /// read loop, latest-frame storage and capture-snapshot (tee) semantics, so the
    /// runtime logic stays hardware-independent and unit-testable.
    /// </summary>
    public interface IThermalFrameSource : IDisposable
    {
        /// <summary>Opens the underlying camera handle. Throws on failure. Idempotent.</summary>
        void Open();

        /// <summary>
        /// Reads the next frame, or null when no frame is available this tick (the caller
        /// retries after a short delay). May block briefly on real hardware.
        /// </summary>
        ThermalFrame? Read();

        /// <summary>Releases the camera handle. Safe to call when not open. Re-openable.</summary>
        void Close();
    }
}
