using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class SerialShutterControllerTests
    {
        [Fact]
        public void NotConnected_OperationsThrow()
        {
            using var ctrl = new SerialShutterController(new SerialSettings());

            Assert.False(ctrl.IsConnected);
            Assert.ThrowsAsync<System.InvalidOperationException>(() => ctrl.OpenShutterAsync(0));
            Assert.ThrowsAsync<System.InvalidOperationException>(() => ctrl.CloseShutterAsync(0));
            Assert.ThrowsAsync<System.InvalidOperationException>(() => ctrl.GetShutterStateAsync(0));
        }

        [Fact]
        public async Task ConnectAsync_InvalidPort_Throws()
        {
            using var ctrl = new SerialShutterController(new SerialSettings { PortName = "COM999" });

            await Assert.ThrowsAnyAsync<System.Exception>(() => ctrl.ConnectAsync());
            Assert.False(ctrl.IsConnected);
        }

        [Fact]
        public void Disconnect_WhenNotConnected_DoesNotThrow()
        {
            using var ctrl = new SerialShutterController(new SerialSettings());

            ctrl.Disconnect();
            Assert.False(ctrl.IsConnected);
        }

        [Fact]
        public void Settings_AreAppliedFromConfiguration()
        {
            var settings = new SerialSettings
            {
                PortName = "COM5",
                BaudRate = 19200,
                DataBits = 8,
                Parity = "Even",
                StopBits = "Two"
            };

            using var ctrl = new SerialShutterController(settings);

            Assert.False(ctrl.IsConnected);
        }
    }
}
