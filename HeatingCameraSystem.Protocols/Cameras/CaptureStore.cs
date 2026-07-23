using System;
using System.Collections.Generic;
using System.IO;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// Facade over <see cref="ThermalCaptureWriter"/> (files) and <see cref="ICaptureIndex"/>
    /// (index): one <see cref="Save"/> writes the radiometric files and records the index entry;
    /// <see cref="Delete"/> and <see cref="Purge"/> remove both files and index entry together.
    /// </summary>
    public sealed class CaptureStore : IDisposable
    {
        private readonly ThermalCaptureWriter _writer;
        private readonly ICaptureIndex _index;

        public CaptureStore(string rootDir, ICaptureIndex index)
        {
            _writer = new ThermalCaptureWriter(rootDir);
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
                // best effort: file may be locked or already removed
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }
        }

        public void Dispose() => _index.Dispose();
    }
}
