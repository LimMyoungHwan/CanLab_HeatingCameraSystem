using System.Linq;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    public class FakeUsbSerialEnumeratorTests
    {
        [Fact]
        public void Enumerate_ReturnsTwoPorts()
        {
            var e = new FakeUsbSerialEnumerator();

            var ports = e.Enumerate();

            Assert.Equal(2, ports.Count);
        }

        [Fact]
        public void Enumerate_PortNamesAreCom7AndCom8()
        {
            var e = new FakeUsbSerialEnumerator();

            var names = e.Enumerate().Select(p => p.PortName).ToArray();

            Assert.Contains("COM7", names);
            Assert.Contains("COM8", names);
        }

        [Fact]
        public void Enumerate_UsbParentIdsAreCamAAndCamB_AndDistinct()
        {
            var e = new FakeUsbSerialEnumerator();

            var parents = e.Enumerate().Select(p => p.UsbParentId).ToArray();

            Assert.Contains(@"USB\VID_0483&PID_5740\CAMA", parents);
            Assert.Contains(@"USB\VID_0483&PID_5740\CAMB", parents);
            Assert.Equal(2, parents.Distinct().Count());
        }
    }
}
