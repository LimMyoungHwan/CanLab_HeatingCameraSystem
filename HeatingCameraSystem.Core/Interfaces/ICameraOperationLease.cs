using System;
using System.Threading;

namespace HeatingCameraSystem.Core.Interfaces;

/// <summary>로컬 UI와 원격 촬영이 같은 카메라 작업 잠금을 공유한다. 재바인딩 시 토큰이 취소된다.</summary>
public interface ICameraOperationLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }
}
