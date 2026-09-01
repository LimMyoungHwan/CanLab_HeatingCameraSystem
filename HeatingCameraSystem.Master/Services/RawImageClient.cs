using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Master.Services
{
    /// <summary>
    /// Agent에 원본(.y16)을 온디맨드로 요청하고 응답을 기다린다. Agent별로 응답 구독을 1회만 걸며
    /// (해제 API가 없다), 요청은 RequestId로 짝지어 동시 요청이 섞이지 않게 한다.
    /// 대상 Agent가 꺼져 있으면 응답이 오지 않으므로 반드시 타임아웃으로 끝낸다.
    /// </summary>
    public sealed class RawImageClient
    {
        private readonly INatsCommunicationService _nats;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<RawImageResponseMessage>> _waiters = new();
        private readonly ConcurrentDictionary<string, bool> _subscribed = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _subscribeGate = new(1, 1);

        public RawImageClient(INatsCommunicationService nats, TimeSpan? timeout = null)
        {
            _nats = nats;
            _timeout = timeout ?? TimeSpan.FromSeconds(20);
        }

        public async Task<RawImageResponseMessage> RequestAsync(string agentId, string rawPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(agentId))
                return Failed("AgentId가 비어 있어 원본을 요청할 수 없습니다.");
            if (string.IsNullOrWhiteSpace(rawPath))
                return Failed("이 캡처에는 Agent 원본 경로가 기록돼 있지 않습니다.");

            await EnsureSubscribedAsync(agentId).ConfigureAwait(false);

            string requestId = Guid.NewGuid().ToString();
            var waiter = new TaskCompletionSource<RawImageResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[requestId] = waiter;

            try
            {
                await _nats.PublishRawImageRequestAsync(new RawImageRequestMessage
                {
                    AgentId = agentId,
                    RequestId = requestId,
                    RawPath = rawPath,
                    Timestamp = DateTime.UtcNow
                }).ConfigureAwait(false);

                Task finished = await Task.WhenAny(waiter.Task, Task.Delay(_timeout, cancellationToken)).ConfigureAwait(false);
                return finished == waiter.Task
                    ? waiter.Task.Result
                    : Failed($"{agentId} 원본 응답 시간 초과({_timeout.TotalSeconds:0}초). Agent가 실행 중인지 확인하세요.");
            }
            catch (Exception ex)
            {
                return Failed($"원본 요청 실패: {ex.Message}");
            }
            finally
            {
                _waiters.TryRemove(requestId, out _);
            }
        }

        private async Task EnsureSubscribedAsync(string agentId)
        {
            if (_subscribed.ContainsKey(agentId)) return;

            await _subscribeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_subscribed.ContainsKey(agentId)) return;
                await _nats.SubscribeRawImageResponseAsync(agentId, response =>
                {
                    if (_waiters.TryGetValue(response.RequestId, out var waiter))
                        waiter.TrySetResult(response);
                }).ConfigureAwait(false);
                _subscribed[agentId] = true;
            }
            finally
            {
                _subscribeGate.Release();
            }
        }

        private static RawImageResponseMessage Failed(string message) =>
            new() { IsSuccess = false, Message = message };
    }
}
