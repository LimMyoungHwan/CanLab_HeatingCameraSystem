using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    public class UsbTopologyTests
    {
        [Fact]
        public void NormalizeParent_TwoInterfacesOfOneDevice_ShareSameKey()
        {
            var mi00 = UsbTopology.NormalizeParent(@"USB\VID_0483&PID_5740&MI_00\6&1a2b&0&0000");
            var mi01 = UsbTopology.NormalizeParent(@"USB\VID_0483&PID_5740&MI_01\6&1a2b&0&0001");

            Assert.Equal(mi00, mi01);
        }

        [Fact]
        public void NormalizeParent_NonMiPath_ReturnsStableCompositeKey()
        {
            var key = UsbTopology.NormalizeParent(@"USB\VID_0483&PID_5740\CAMA");

            Assert.Equal(@"USB\VID_0483&PID_5740", key);
        }

        [Fact]
        public void NormalizeParent_IsCaseInsensitive()
        {
            var lower = UsbTopology.NormalizeParent(@"usb\vid_0483&pid_5740&mi_00\6&1a2b&0&0000");
            var upper = UsbTopology.NormalizeParent(@"USB\VID_0483&PID_5740&MI_00\6&1A2B&0&0000");

            Assert.Equal(upper, lower);
        }
    }
}
