using System.Linq;
using HeatingCameraSystem.Protocols.Simulation;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    public class FakeCameraEnumeratorTests
    {
        [Fact]
        public void Enumerate_ReturnsTwoCameras()
        {
            var e = new FakeCameraEnumerator();

            var cams = e.Enumerate();

            Assert.Equal(2, cams.Count);
        }

        [Fact]
        public void Enumerate_UsbParentIdsAreCamAAndCamB_AndDistinct()
        {
            var e = new FakeCameraEnumerator();

            var parents = e.Enumerate().Select(c => c.UsbParentId).ToArray();

            Assert.Contains(@"USB\VID_0483&PID_5740\CAMA", parents);
            Assert.Contains(@"USB\VID_0483&PID_5740\CAMB", parents);
            Assert.Equal(2, parents.Distinct().Count());
        }

        [Fact]
        public void EnumerateThermal_FiltersToCltcTVgaPrefixedCameras()
        {
            var e = new FakeCameraEnumerator();

            var thermal = e.EnumerateThermal();

            Assert.Equal(2, thermal.Count);
            Assert.All(thermal, c => Assert.StartsWith("CLTC_T_VGA", c.FriendlyName));
        }

        [Fact]
        public void EnumerateThermal_NonMatchingPrefix_ReturnsEmpty()
        {
            var e = new FakeCameraEnumerator();

            var thermal = e.EnumerateThermal("NO_SUCH_PREFIX");

            Assert.Empty(thermal);
        }
    }
}
