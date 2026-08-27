using System;
using System.Collections.Generic;
using System.IO;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// <see cref="ThermalCaptureWriter"/>(파일)와 <see cref="ICaptureIndex"/>(인덱스)를 묶는 파사드.
    /// <see cref="Save"/> 한 번으로 방사 측정 파일 기록과 인덱스 등록을 함께 하고,
    /// <see cref="Delete"/>·<see cref="Purge"/>는 파일과 인덱스 항목을 같이 제거한다.
    /// </summary>
    public sealed class CaptureStore : IDisposable
    {
        private readonly ThermalCaptureWriter _writer;
        private readonly ICaptureIndex _index;

        public CaptureStore(string rootDir, ICaptureIndex index, CaptureImageFormat format = CaptureImageFormat.Y16Raw)
        {
            _writer = new ThermalCaptureWriter(rootDir, format);
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public CaptureRecord Save(ThermalFrame frame, string agentId, int cameraIndex, string? recipeStepId = null)
        {
            CaptureFiles files = _writer.Write(frame, agentId, cameraIndex, recipeStepId);

            var record = new CaptureRecord
            {
                Id = Guid.NewGuid(),
                AgentId = files.Metadata.AgentId,
                CameraIndex = files.Metadata.CameraIndex,
                TimestampUtc = files.Metadata.TimestampUtc.UtcDateTime,
                Width = files.Metadata.Width,
                Height = files.Metadata.Height,
                Min = files.Metadata.Min,
                Max = files.Metadata.Max,
                RecipeStepId = files.Metadata.RecipeStepId,
                Y16Path = files.Y16Path,
                JsonPath = files.JsonPath,
                PngPath = files.PngPath
            };

            _index.Add(record);
            return record;
        }

        public IReadOnlyList<CaptureRecord> Query(string? agentId = null, int limit = 200)
            => _index.Query(agentId, limit);

        /// <summary>레코드의 파일과 인덱스 항목을 함께 제거한다. 인덱스에 없는 id면 false를 반환한다.</summary>
        public bool Delete(Guid id)
        {
            CaptureRecord? record = _index.Get(id);
            if (record is null)
            {
                return false;
            }

            DeleteFiles(record);
            return _index.Delete(id);
        }

        /// <summary>cutoffUtc 이전 레코드를 파일까지 포함해 정리하고 제거한 건수를 반환한다.</summary>
        public int Purge(DateTime cutoffUtc)
        {
            int removed = 0;
            foreach (CaptureRecord record in _index.FindOlderThan(cutoffUtc))
            {
                DeleteFiles(record);
                if (_index.Delete(record.Id))
                {
                    removed++;
                }
            }

            return removed;
        }

        private static void DeleteFiles(CaptureRecord record)
        {
            TryDelete(record.Y16Path);
            TryDelete(record.JsonPath);
            TryDelete(Path.ChangeExtension(record.Y16Path, ".tif"));
            if (!string.IsNullOrEmpty(record.PngPath))
            {
                TryDelete(record.PngPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // 최선 노력: 파일이 잠겨 있거나 이미 지워졌을 수 있다.
            }
            catch (UnauthorizedAccessException)
            {
                // 최선 노력
            }
        }

        public void Dispose() => _index.Dispose();
    }
}
