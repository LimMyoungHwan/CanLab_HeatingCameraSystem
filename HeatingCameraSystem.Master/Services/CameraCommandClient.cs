using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services;

/// <summary>수동 카메라 명령의 응답을 요청·카메라·동작에 연결하고 취소/시간 초과를 처리한다.</summary>
public sealed class CameraCommandClient : IAsyncDisposable
{
    private sealed class Pending(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly INatsCommunicationService _nats;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _abortTimeout;
    private readonly int _biasAttemptTimeoutSeconds;
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private int _stopping;

    public CameraCommandClient(INatsCommunicationService nats, int biasAttemptTimeoutSeconds = 120,
        TimeSpan? commandTimeout = null, TimeSpan? abortTimeout = null)
    {
        _nats = nats;
        _biasAttemptTimeoutSeconds = BiasExecutionPolicy.NormalizeAttemptTimeoutSeconds(biasAttemptTimeoutSeconds);
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        _abortTimeout = abortTimeout ?? TimeSpan.FromSeconds(3);
    }

    public async Task<CameraControlAckMessage> SendAsync(string agentId, int cameraIndex, string operation,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _stopping) != 0) throw new OperationCanceledException("카메라 제어를 종료하고 있습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        string requestId = Guid.NewGuid().ToString("N");
        bool isBias = BiasExecutionPolicy.IsBiasOperation(operation) || operation == CameraControlOps.BiasUseCurrent;
        TimeSpan timeout = isBias ? BiasExecutionPolicy.ResponseTimeout(_biasAttemptTimeoutSeconds) : _commandTimeout;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new Pending(stop);
        _pending[requestId] = pending;
        if (Volatile.Read(ref _stopping) != 0) stop.Cancel();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(timeout);
        var response = new TaskCompletionSource<CameraControlAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool published = false;
        try
        {
            await using IAsyncDisposable subscription = await _nats.SubscribeCameraControlAckScopedAsync(agentId, ack =>
            {
                if (ack.RequestId == requestId && ack.AgentId == agentId && ack.CameraIndex == cameraIndex && ack.Op == operation)
                    response.TrySetResult(ack);
            }, deadline.Token).ConfigureAwait(false);

            deadline.Token.ThrowIfCancellationRequested();
            published = true;
            await _nats.PublishCameraControlAsync(new CameraControlMessage
            {
                AgentId = agentId, CameraIndex = cameraIndex, Op = operation, RequestId = requestId,
                BiasAttemptTimeoutSeconds = _biasAttemptTimeoutSeconds, Timestamp = DateTime.UtcNow
            }).WaitAsync(deadline.Token).ConfigureAwait(false);
            CameraControlAckMessage ack = await response.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            return ack;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            if (published) await AbortAsync(agentId, cameraIndex, operation, requestId).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            if (published) await AbortAsync(agentId, cameraIndex, operation, requestId).ConfigureAwait(false);
            throw new TimeoutException($"{agentId} {operation} 응답 시간 초과({timeout.TotalSeconds:0.#}초).", ex);
        }
        catch
        {
            if (published) await AbortAsync(agentId, cameraIndex, operation, requestId).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
            pending.Completed.TrySetResult();
        }
    }

    private async Task AbortAsync(string agentId, int cameraIndex, string operation, string requestId)
    {
        string? abort = BiasExecutionPolicy.IsBiasOperation(operation) || operation == CameraControlOps.BiasUseCurrent
            ? CameraControlOps.BiasAbort : operation == CameraControlOps.Capture ? CameraControlOps.CaptureAbort : null;
        if (abort is null) return;
        try
        {
            await _nats.PublishCameraControlAsync(new CameraControlMessage
            {
                AgentId = agentId, CameraIndex = cameraIndex, Op = abort,
                RequestId = Guid.NewGuid().ToString("N"), TargetRequestId = requestId, Timestamp = DateTime.UtcNow
            }).WaitAsync(_abortTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AlarmSink.Raise(AlarmCodes.CaptureAbortNoAck, AlarmSeverity.Error, "Camera",
                $"{agentId} {operation} 취소 전송 실패: {ex.Message}");
        }
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        Interlocked.Exchange(ref _stopping, 1);
        Pending[] pending = _pending.Values.ToArray();
        foreach (Pending item in pending)
        {
            try { item.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        try { await Task.WhenAll(pending.Select(item => item.Completed.Task)).WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { System.Diagnostics.Debug.WriteLine("[Camera] pending commands did not finish before shutdown timeout."); }
    }

    public ValueTask DisposeAsync() => new(StopAsync(TimeSpan.FromSeconds(5)));
}
