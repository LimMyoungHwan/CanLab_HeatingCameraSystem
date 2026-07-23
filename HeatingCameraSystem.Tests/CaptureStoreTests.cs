using System;
using System.IO;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CaptureStoreTests
    {
        [Fact]
        public void Save_Index_Reconstruct_And_Purge()
        {
            string dir = Path.Combine(Path.GetTempPath(), "hcs_store_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string dbPath = Path.Combine(dir, "index.db");
                using (var index = new LiteDbCaptureIndex(dbPath))
                using (var store = new CaptureStore(dir, index))
                {
                    var frame = new ThermalFrame(new ushort[] { 1, 2, 3, 4 }, 2, 2, DateTimeOffset.Now);
                    CaptureRecord rec = store.Save(frame, "cam0", cameraIndex: 0, recipeStepId: "s1");

                    Assert.True(File.Exists(rec.Y16Path));
                    Assert.True(File.Exists(rec.JsonPath));

                    var all = store.Query();
                    Assert.Single(all);
                    Assert.Equal("cam0", all[0].AgentId);
                    Assert.Equal("s1", all[0].RecipeStepId);

                    // Preview reconstructed from raw .y16 (no persisted PNG needed).
                    ThermalFrame back = ThermalFrameReader.Read(rec);
                    Assert.Equal(4, back.Pixels.Length);
                    Assert.Equal((ushort)3, back.Pixels[2]);

                    int purged = store.Purge(DateTime.UtcNow.AddDays(1));
                    Assert.Equal(1, purged);
                    Assert.False(File.Exists(rec.Y16Path));
                    Assert.False(File.Exists(rec.JsonPath));
                    Assert.Empty(store.Query());
                }
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
