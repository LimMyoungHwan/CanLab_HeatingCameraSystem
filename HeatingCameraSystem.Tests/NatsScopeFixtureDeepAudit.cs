using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using Moq;

namespace HeatingCameraSystem.Tests;

internal sealed class NatsScopeFixtureDeepAudit
{
    private sealed class Lease : IAsyncDisposable
    {
        private readonly Action<Lease> _remove;
        public string Subject { get; }
        public Delegate? Callback { get; private set; }
        public Lease(string subject, Delegate callback, Action<Lease> remove)
        { Subject = subject; Callback = callback; _remove = remove; }
        public ValueTask DisposeAsync() { Callback = null; _remove(this); return ValueTask.CompletedTask; }
    }

    private readonly List<Lease> _leases = new();
    private readonly object _sync = new();
    public Mock<INatsCommunicationService> Nats { get; } = new();
    public int ActiveCount { get { lock (_sync) return _leases.Count; } }

    public NatsScopeFixtureDeepAudit()
    {
        Register<CameraControlAckMessage>();
        Register<CaptureResultMessage>();
        Register<AgentStatusMessage>();
        Register<LiveFrameMessage>();
        Register<AgentConfigSnapshotMessage>();
        Register<AgentConfigAckMessage>();
        Register<RawImageResponseMessage>();
    }

    private void Register<T>()
    {
        Nats.As<INatsScopedSubscriptions>()
            .Setup(service => service.SubscribeAsync(It.IsAny<string>(), It.IsAny<Action<T>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Action<T>, CancellationToken>((subject, callback, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var lease = new Lease(subject, callback, item => { lock (_sync) _leases.Remove(item); });
                lock (_sync) _leases.Add(lease);
                return Task.FromResult<IAsyncDisposable>(lease);
            });
    }

    public void Emit<T>(string subject, T message)
    {
        Lease[] leases;
        lock (_sync) leases = _leases.ToArray();
        foreach (Lease lease in leases)
        {
            bool matches = lease.Subject == subject || (lease.Subject.EndsWith('>')
                && subject.StartsWith(lease.Subject[..^1], StringComparison.Ordinal));
            if (matches && lease.Callback is Action<T> callback) callback(message);
        }
    }
}
