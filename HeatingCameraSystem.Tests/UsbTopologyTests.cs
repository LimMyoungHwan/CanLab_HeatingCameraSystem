using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class UsbTopologyTests
    {
        [Fact]
        public void DevicePathToInstanceId_ConvertsRealDShowPathToEnumKey()
        {
            string devicePath =
                @"@device:pnp:\\?\usb#vid_04b4&pid_00f9&mi_00#8&2b9062bf&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";

            string instanceId = UsbTopology.DevicePathToInstanceId(devicePath);

            Assert.Equal(@"USB\VID_04B4&PID_00F9&MI_00\8&2B9062BF&0&0000", instanceId);
        }

        [Fact]
        public void DevicePathToInstanceId_HandlesBareRootPath()
        {
            string devicePath =
                @"\\?\usb#vid_2560&pid_c110&mi_00#7&2a5708c6&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";

            string instanceId = UsbTopology.DevicePathToInstanceId(devicePath);

            Assert.Equal(@"USB\VID_2560&PID_C110&MI_00\7&2A5708C6&0&0000", instanceId);
        }

        [Fact]
        public void DevicePathToInstanceId_EmptyReturnsEmpty()
        {
            Assert.Equal(string.Empty, UsbTopology.DevicePathToInstanceId(string.Empty));
        }
    }
}
