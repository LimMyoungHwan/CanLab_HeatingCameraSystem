using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    /// <summary>
    /// 캡처 파일의 인덱스를 메모리에서 관리한다. Agent가 보낸 캡처 결과를 등록하고,
    /// AgentId/시간으로 필터링해 Master가 파일을 찾을 수 있게 한다.
    /// </summary>
    public interface ICaptureIndex : IDisposable
    {
        /// <summary>캡처 기록을 인덱스에 추가한다.</summary>
        void Add(CaptureRecord record);

        /// <summary>AgentId로 필터링한 캡처 기록 목록을 반환한다. agentId가 null이면 전체.</summary>
        IReadOnlyList<CaptureRecord> Query(string? agentId = null, int limit = 200);

        /// <summary>ID로 단일 캡처 기록을 조회한다. 없으면 null.</summary>
        CaptureRecord? Get(Guid id);

        /// <summary>ID로 캡처 기록을 삭제한다. 삭제 성공 여부를 반환한다.</summary>
        bool Delete(Guid id);

        /// <summary>지정 시점보다 오래된 캡처 기록 목록을 반환한다.</summary>
        IReadOnlyList<CaptureRecord> FindOlderThan(DateTime cutoffUtc);
    }
}
