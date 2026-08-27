using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// AgentUI 프로세스 하나의 모든 로컬 <see cref="ICameraRuntime"/>을 소유한다 — 물리 카메라당
    /// 런타임 하나(인덱스가 서로 달라 핸들 경합 없음). 카메라 한 대의 시작/정지는 격리되어,
    /// 하나가 시작에 실패하거나 Faulted가 되어도 나머지는 절대 멈추지 않는다. 실제 프레임 소스
    /// (실제 Y16 vs 시뮬레이션)는 주입된 팩터리가 고르므로 이 매니저는 하드웨어와 무관하고
    /// 단위 테스트 가능하다.
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

        /// <summary>디스크립터로 런타임을 만들어 등록한다. 시작은 하지 않는다.</summary>
        public ICameraRuntime Add(CameraDescriptor descriptor)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

            var runtime = _factory(descriptor);
            lock (_gate)
            {
                if (_runtimes.TryGetValue(descriptor.AgentId, out var existing))
                {
                    // 교체: 드문 경로라 묵은 런타임을 여기서 바로 Dispose해도 된다.
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

        /// <summary>등록된 모든 런타임을 시작한다. 한 카메라의 실패는 격리된다.</summary>
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
                    // 격리: 실패한 런타임은 자체 상태로 Faulted를 보고하고, 나머지는 계속 돈다.
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
                    // 최선 노력
                }
            }
        }

        /// <summary>
        /// 카메라 런타임 하나를 정지·해제·제거한다 — Reject/Disable이 쓰는 카메라별 "언로드"로,
        /// 카메라 한 대가 프로세스 전체를 무너뜨리지 않게 한다(S7 참조).
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
                catch { /* 최선 노력 */ }
            }
        }
    }
}
