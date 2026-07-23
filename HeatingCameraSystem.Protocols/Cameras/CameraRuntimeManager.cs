using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Owns all local <see cref="ICameraRuntime"/> instances for one AgentUI process — one
    /// runtime per physical camera (distinct indices, no handle contention). Start/stop of a
    /// single camera is isolated: one camera failing to start or faulting never stops the
    /// others. The concrete frame source (real Y16 vs simulation) is chosen by the injected
    /// factory, keeping this manager hardware-independent and unit-testable.
    /// </summary>
    public sealed class CameraRuntimeManager : IDisposable
    {
        private readonly Func<CameraDescriptor, ICameraRuntime> _factory;
        private readonly object _gate = new();
        private readonly Dictionary<string, ICameraRuntime> _runtimes = new(StringComparer.Ordinal);

        public CameraRuntimeManager(Func<CameraDescriptor, ICameraRuntime> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public IReadOnlyList<ICameraRuntime> Runtimes
        {
            get { lock (_gate) return _runtimes.Values.ToList(); }
        }

        public int Count
        {
            get { lock (_gate) return _runtimes.Count; }
        }

        /// <summary>Creates and registers a runtime for the descriptor. Does not start it.</summary>
        public ICameraRuntime Add(CameraDescriptor descriptor)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

            var runtime = _factory(descriptor);
            lock (_gate)
            {
                if (_runtimes.TryGetValue(descriptor.AgentId, out var existing))
                {
                    // Replace: dispose the stale one outside the hot path is fine here (rare).
                    _runtimes[descriptor.AgentId] = runtime;
                    existing.Dispose();
                }
                else
                {
                    _runtimes[descriptor.AgentId] = runtime;
                }
            }

            return runtime;
        }

        public bool TryGet(string agentId, out ICameraRuntime runtime)
        {
            lock (_gate)
            {
                return _runtimes.TryGetValue(agentId, out runtime!);
            }
        }

        /// <summary>Starts every registered runtime. A failure on one camera is isolated.</summary>
        public async Task StartAllAsync()
        {
            foreach (var runtime in Runtimes)
            {
                try
                {
                    await runtime.StartAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Isolated: the faulting runtime reports Faulted via its own status;
                    // siblings keep running.
                }
            }
        }

        public async Task StopAllAsync()
        {
            foreach (var runtime in Runtimes)
            {
                try
                {
                    await runtime.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
        }

        /// <summary>
        /// Stops, disposes and removes a single camera runtime — the per-camera "unload" used
        /// by Reject/Disable so one camera never takes down the whole process (see S7).
        /// </summary>
        public void Remove(string agentId)
        {
            ICameraRuntime? runtime;
            lock (_gate)
            {
                if (!_runtimes.Remove(agentId, out runtime))
                {
                    return;
                }
            }

            runtime?.Dispose();
        }

        public void Dispose()
        {
            List<ICameraRuntime> snapshot;
            lock (_gate)
            {
                snapshot = _runtimes.Values.ToList();
                _runtimes.Clear();
            }

            foreach (var runtime in snapshot)
            {
                try { runtime.Dispose(); }
                catch { /* best effort */ }
            }
        }
    }
}
