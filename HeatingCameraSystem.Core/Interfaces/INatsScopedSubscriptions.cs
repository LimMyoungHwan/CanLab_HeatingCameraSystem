using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>서비스 전체보다 짧은 구독 수명. 완료 시 서버에 구독이 준비되어 있다.</summary>
    public interface INatsScopedSubscriptions
    {
        Task<IAsyncDisposable> SubscribeAsync<T>(string subject, Action<T> onMessageReceived,
            CancellationToken cancellationToken = default);
    }
}
