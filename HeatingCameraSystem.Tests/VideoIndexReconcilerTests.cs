using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols.Cameras;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class VideoIndexReconcilerTests
    {
        private static CameraDescriptor Cam(string agentId, int index, string containerId) =>
            new(agentId, index, agentId, UsbContainerId: containerId);

        private static VideoDevice Dev(int index, string containerId) =>
            new(index, $"path-{index}", containerId, $"CLTC_T_VGA {index}");

        [Fact]
        public void RebindsIndexWhenContainerMatches()
        {
            var cams = new List<CameraDescriptor> { Cam("Agent_1", 0, "C1") };
            var devices = new List<VideoDevice> { Dev(2, "C1") };

            int changed = VideoIndexReconciler.Reconcile(cams, devices);

            Assert.Equal(1, changed);
            Assert.Equal(2, cams[0].OpenCvIndex);
        }

        [Fact]
        public void LeavesIndexWhenAlreadyCorrect()
        {
            var cams = new List<CameraDescriptor> { Cam("Agent_1", 3, "C1") };
            var devices = new List<VideoDevice> { Dev(3, "C1") };

            int changed = VideoIndexReconciler.Reconcile(cams, devices);

            Assert.Equal(0, changed);
            Assert.Equal(3, cams[0].OpenCvIndex);
        }

        [Fact]
        public void SkipsCameraWithoutContainerId()
        {
            var cams = new List<CameraDescriptor> { Cam("Agent_1", 0, "") };
            var devices = new List<VideoDevice> { Dev(2, "C1") };

            int changed = VideoIndexReconciler.Reconcile(cams, devices);

            Assert.Equal(0, changed);
            Assert.Equal(0, cams[0].OpenCvIndex);
        }

        [Fact]
        public void SkipsWhenNoDeviceMatchesContainer()
        {
            var cams = new List<CameraDescriptor> { Cam("Agent_1", 0, "C1") };
            var devices = new List<VideoDevice> { Dev(2, "C2") };

            int changed = VideoIndexReconciler.Reconcile(cams, devices);

            Assert.Equal(0, changed);
            Assert.Equal(0, cams[0].OpenCvIndex);
        }

        [Fact]
        public void SwapsTwoCamerasWhenPortsReordered()
        {
            var cams = new List<CameraDescriptor>
            {
                Cam("Agent_1", 0, "C1"),
                Cam("Agent_2", 1, "C2"),
            };
            var devices = new List<VideoDevice> { Dev(1, "C1"), Dev(0, "C2") };

            int changed = VideoIndexReconciler.Reconcile(cams, devices);

            Assert.Equal(2, changed);
            Assert.Equal(1, cams[0].OpenCvIndex);
            Assert.Equal(0, cams[1].OpenCvIndex);
        }
    }
}
