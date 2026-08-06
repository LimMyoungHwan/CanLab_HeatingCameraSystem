using System.Collections.Generic;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Core.Interfaces
{
    // Enumerates video-input devices in the SAME order OpenCV's DirectShow backend assigns integer
    // indices, so a port-stable ContainerId can be resolved to the current OpenCV index.
    public interface IVideoDeviceEnumerator
    {
        IReadOnlyList<VideoDevice> Enumerate();
    }
}
