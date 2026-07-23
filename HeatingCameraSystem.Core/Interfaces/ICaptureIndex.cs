using System;
using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    public interface ICaptureIndex : IDisposable
    {
        void Add(CaptureRecord record);
        IReadOnlyList<CaptureRecord> Query(string? agentId = null, int limit = 200);
        CaptureRecord? Get(Guid id);
        bool Delete(Guid id);
        IReadOnlyList<CaptureRecord> FindOlderThan(DateTime cutoffUtc);
    }
}
