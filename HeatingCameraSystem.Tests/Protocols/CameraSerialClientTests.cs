using System.Threading.Tasks;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    public class CameraSerialClientTests
    {
        [Fact]
        public async Task Fake_Com7_Initializes_Reads_And_Controls()
        {
            using var c = new FakeCameraSerialClient("COM7");
            await c.InitializeAsync();

            Assert.True(c.IsOpen);
            Assert.Equal("000100001", await c.ReadSerialNumberAsync());

            double temp = await c.ReadFpaTemperatureAsync();
            Assert.True(double.IsFinite(temp));

            await c.SetShutterAsync(true);
        }
    }
}
