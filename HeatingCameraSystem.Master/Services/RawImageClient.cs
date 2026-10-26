using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>원본 요청마다 응답 구독의 준비를 기다리고, 완료·취소·시간 초과 시 해제한다.</summary>
    public sealed class RawImageClient
    {
        private readonly INatsCommunicationService _nats;
        private readonly TimeSpan _timeout;

        public RawImageClient(INatsCommunicationService nats, TimeSpan? timeout = null)
        {
            _nats = nats;
            _timeout = timeout ?? TimeSpan.FromSeconds(20);
        }

        public async Task<RawImageResponseMessage> RequestAsync(string agentId, string rawPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentId)) return Failed("AgentId가 비어 있어 원본을 요청할 수 없습니다.");
            if (string.IsNullOrWhiteSpace(rawPath)) return Failed("이 캡처에는 Agent 원본 경로가 기록돼 있지 않습니다.");
            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            string requestId = Guid.NewGuid().ToString("N");
            var waiter = new TaskCompletionSource<RawImageResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await using IAsyncDisposable subscription = await _nats.SubscribeRawImageResponseScopedAsync(agentId, response =>
                {
                    if (response.AgentId == agentId && response.RequestId == requestId) waiter.TrySetResult(response);
                }, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                await _nats.PublishRawImageRequestAsync(new RawImageRequestMessage
                {
                    AgentId = agentId, RequestId = requestId, RawPath = rawPath, Timestamp = DateTime.UtcNow
                }).WaitAsync(deadline.Token).ConfigureAwait(false);
                return await waiter.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                return Failed($"{agentId} 원본 응답 시간 초과({_timeout.TotalSeconds:0}초). Agent가 실행 중인지 확인하세요.");
            }
            catch (Exception ex) { return Failed($"원본 요청 실패: {ex.Message}"); }
        }

        private static RawImageResponseMessage Failed(string message) => new() { IsSuccess = false, Message = message };
    }
}
