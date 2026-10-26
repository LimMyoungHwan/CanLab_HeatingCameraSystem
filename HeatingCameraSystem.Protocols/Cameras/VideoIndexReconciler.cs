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
        /// <summary>
        /// 현재 유일하게 확인된 물리 장치에만 인덱스를 배정한다. 미존재/모호/중복은 -1(offline)로
        /// 표시한다. 저장된 AgentId/SN은 유지하며, 호출자는 -1을 native camera open에 넘기면 안 된다.
        /// </summary>
        public static int Reconcile(IList<CameraDescriptor> cameras, IReadOnlyList<VideoDevice> devices)
        {
            var indices = new int[cameras.Count];
            for (int i = 0; i < cameras.Count; i++)
            {
                CameraDescriptor cam = cameras[i];
                var matches = string.IsNullOrWhiteSpace(cam.UsbContainerId)
                    ? new List<VideoDevice>()
                    : devices.Where(d => string.Equals(d.ContainerId, cam.UsbContainerId, StringComparison.OrdinalIgnoreCase)).ToList();
                indices[i] = matches.Count == 1 && matches[0].Index >= 0
                    && devices.Count(d => d.Index == matches[0].Index) == 1 ? matches[0].Index : -1;
            }
            var duplicateIndices = indices.Where(index => index >= 0)
                .GroupBy(index => index).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            int changed = 0;
            for (int i = 0; i < cameras.Count; i++)
            {
                CameraDescriptor cam = cameras[i];
                int index = duplicateIndices.Contains(indices[i]) ? -1 : indices[i];
                if (index == cam.OpenCvIndex) continue;
                cameras[i] = cam with { OpenCvIndex = index };
                changed++;
            }

            return changed;
        }
    }
}
