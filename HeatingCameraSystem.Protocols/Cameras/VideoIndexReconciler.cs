using System;
using System.Collections.Generic;
using System.Linq;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// (이미 시리얼 리컨사일을 거친) UsbContainerId를 라이브 비디오 열거와 대조해 각 카메라의
    /// OpenCvIndex를 현재 DirectShow 인덱스로 다시 쓴다. 시리얼/S-N 리컨사일이 UsbContainerId를
    /// 갱신한 뒤인 시작 시와 매 핫플러그마다 실행해야, 재연결로 DShow 열거 순서가 섞여도
    /// OpenCV가 올바른 물리 카메라를 연다.
    /// </summary>
    public static class VideoIndexReconciler
    {
        /// <summary>리스트를 제자리에서 수정하며, 바뀐 OpenCvIndex 개수를 반환한다.</summary>
        public static int Reconcile(IList<CameraDescriptor> cameras, IReadOnlyList<VideoDevice> devices)
        {
            int changed = 0;
            for (int i = 0; i < cameras.Count; i++)
            {
                CameraDescriptor cam = cameras[i];
                if (string.IsNullOrWhiteSpace(cam.UsbContainerId))
                {
                    continue;
                }

                VideoDevice? dev = devices.FirstOrDefault(
                    d => string.Equals(d.ContainerId, cam.UsbContainerId, StringComparison.OrdinalIgnoreCase));
                if (dev is null || dev.Index == cam.OpenCvIndex)
                {
                    continue;
                }

                cameras[i] = cam with { OpenCvIndex = dev.Index };
                changed++;
            }

            return changed;
        }
    }
}
