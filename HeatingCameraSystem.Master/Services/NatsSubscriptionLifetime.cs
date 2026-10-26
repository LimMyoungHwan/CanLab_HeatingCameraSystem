using System.Diagnostics;

namespace HeatingCameraSystem.Master.Services;

/// <summary>화면 수명에 맞춰 준비 중인 구독과 등록된 구독을 함께 정리한다.</summary>
internal sealed class NatsSubscriptionLifetime : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<IAsyncDisposable> _subscriptions = new();
    private readonly object _sync = new();
    private bool _disposed;
    private Task? _disposeTask;

    public CancellationToken Token => _cancellation.Token;

    public async Task AddAsync(Func<CancellationToken, Task<IAsyncDisposable>> subscribe)
    {
        IAsyncDisposable subscription;
        while (true)
        {
            try
            {
                Token.ThrowIfCancellationRequested();
                subscription = await subscribe(Token).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NATS] subscription not ready; retrying: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(1), Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        lock (_sync)
        {
            if (!_disposed)
            {
                _subscriptions.Add(subscription);
                return;
            }
        }
        await subscription.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => _ = DisposeAsync();

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _cancellation.Cancel();
            IAsyncDisposable[] subscriptions = _subscriptions.ToArray();
            _subscriptions.Clear();
            _disposeTask = DisposeSubscriptionsAsync(subscriptions);
            return new ValueTask(_disposeTask);
        }
    }

    private static async Task DisposeSubscriptionsAsync(IEnumerable<IAsyncDisposable> subscriptions)
    {
        foreach (IAsyncDisposable subscription in subscriptions)
        {
            try { await subscription.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Debug.WriteLine($"[NATS] subscription cleanup failed: {ex.Message}"); }
        }
    }
}
