using System;
using System.Buffers.Binary;
using System.IO;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Cameras
{
    /// <summary>
    /// 생산 저장 규칙의 <c>.raw</c> 파일을 쓴다. 16비트 리틀엔디안 픽셀 나열이며 픽셀(0,0)에는
    /// FPA 온도 원시값이 들어간다(<c>참고/AISEN_CODE/main.py:1059</c>).
    /// <para>
    /// 프레임 배열은 건드리지 않고 복사된 바이트만 덮어쓴다 — 호출자가 같은 프레임으로 계산하는
    /// 미리보기·MinMax가 FPA 값 때문에 망가지지 않아야 한다.
    /// </para>
    /// <para>
    /// 파일명이 <c>{접두사}_{일련번호}</c>로 고정이라 같은 이름이 있으면 그대로 덮어쓴다.
    /// 중단 후 재촬영이 자연스럽게 복구되는 근거이며, 접미사를 붙이는
    /// <see cref="ThermalCaptureWriter"/>와 의도적으로 다르다.
    /// </para>
    /// </summary>
    public static class RawCaptureWriter
    {
        public static string Write(string directory, string fileName, ThermalFrame frame, short? fpaRaw)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));

            Directory.CreateDirectory(directory);

            var bytes = new byte[frame.Pixels.Length * sizeof(ushort)];
            Buffer.BlockCopy(frame.Pixels, 0, bytes, 0, bytes.Length);

            if (fpaRaw is short raw && bytes.Length >= sizeof(short))
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes, raw);
            }

            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
