using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.Services;
using LiteDB;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class CameraSerialSettingsTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var s = new CameraSerialSettings();

            Assert.Equal("COM3", s.PortName);
            Assert.Equal(9600,   s.BaudRate);
            Assert.Equal(8,      s.DataBits);
            Assert.Equal("None", s.Parity);
            Assert.Equal("One",  s.StopBits);
        }

        [Fact]
        public async Task Upsert_NewEntry_CanBeRetrieved()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbCameraSerialSettingsRepository(db);

            var settings = new CameraSerialSettings
            {
                CameraIndex = 3,
                PortName    = "COM5",
                BaudRate    = 115200
            };

            await repo.UpsertAsync(settings);
            var result = await repo.GetByCameraIndexAsync(3);

            Assert.NotNull(result);
            Assert.Equal("COM5",   result!.PortName);
            Assert.Equal(115200,   result.BaudRate);
        }

        [Fact]
        public async Task Upsert_SameCameraIndex_Overwrites()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbCameraSerialSettingsRepository(db);

            await repo.UpsertAsync(new CameraSerialSettings { CameraIndex = 1, BaudRate = 9600 });
            await repo.UpsertAsync(new CameraSerialSettings { CameraIndex = 1, BaudRate = 19200 });

            var result = await repo.GetByCameraIndexAsync(1);

            Assert.Equal(19200, result!.BaudRate);
        }

        [Fact]
        public async Task GetAllAsync_ReturnsAllEntries()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbCameraSerialSettingsRepository(db);

            await repo.UpsertAsync(new CameraSerialSettings { CameraIndex = 0 });
            await repo.UpsertAsync(new CameraSerialSettings { CameraIndex = 1 });
            await repo.UpsertAsync(new CameraSerialSettings { CameraIndex = 2 });

            var all = (await repo.GetAllAsync()).ToList();

            Assert.Equal(3, all.Count);
        }

        [Fact]
        public async Task GetByCameraIndex_Missing_ReturnsNull()
        {
            using var db   = new LiteDatabase(new MemoryStream());
            var       repo = new LiteDbCameraSerialSettingsRepository(db);

            var result = await repo.GetByCameraIndexAsync(99);

            Assert.Null(result);
        }
    }
}
