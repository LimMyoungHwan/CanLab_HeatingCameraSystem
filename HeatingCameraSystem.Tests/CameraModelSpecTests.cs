using System;
using System.IO;
using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraModelSpecTests
    {
        [Fact]
        public void Load_ExistingModel_ReturnsSpec()
        {
            string dir = Path.Combine(Path.GetTempPath(), "HCS_ModelTest_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "AISEN.json"), "{\"Width\":640,\"Height\":480}");

            var spec = CameraModelSpec.Load(dir, "AISEN");

            Assert.NotNull(spec);
            Assert.Equal(640, spec!.Width);
            Assert.Equal(480, spec.Height);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void Load_MissingFile_ReturnsNull()
        {
            string dir = Path.Combine(Path.GetTempPath(), "HCS_ModelTest_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);

            var spec = CameraModelSpec.Load(dir, "NoSuchModel");

            Assert.Null(spec);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void Load_NullModelName_ReturnsNull()
        {
            var spec = CameraModelSpec.Load(Path.GetTempPath(), null);
            Assert.Null(spec);
        }

        [Fact]
        public void Load_InvalidJson_ReturnsNull()
        {
            string dir = Path.Combine(Path.GetTempPath(), "HCS_ModelTest_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Broken.json"), "{ not valid json");

            var spec = CameraModelSpec.Load(dir, "Broken");

            Assert.Null(spec);

            Directory.Delete(dir, true);
        }
    }
}
