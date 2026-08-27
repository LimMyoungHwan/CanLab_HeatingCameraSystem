using System;
using System.Buffers.Binary;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// SR-800N VIP 바이너리 프레임 빌더/파서(통신 규격 6057060).
    /// 프레임 = sync(0xAA) + address + size(2바이트) + service + 파라미터 블록들 + 체크섬.
    /// 다바이트 값은 전부 big-endian이다.
    /// </summary>
    public static class SrProtocol
    {
        public const byte Sync = 0xAA;
        public const byte AddressId = 0x01;
        public const byte ServiceSetParameters = 0x06;
        public const byte ServiceGetParameters = 0x08;

        public const ushort ParamOperationMode = 0x07F0;
        public const ushort ParamSetPointAbsolute = 0x07F1;
        public const ushort ParamSetPointDifferential = 0x07F2;
        public const ushort ParamCurrentTemperature = 0x07D7;
        public const ushort ParamCurrentSetPoint = 0x07F3;
        public const ushort ParamTemperatureIsStable = 0x07D5;

        public const byte ModeAbsolute = 1;
        public const byte ModeDifferential = 2;

        public static byte[] SetMode(int mode) => BuildSetByte(ParamOperationMode, (byte)mode);

        public static byte[] SetTemperature(float celsius) => BuildSetFloat(ParamSetPointAbsolute, celsius);

        public static byte[] GetTemperature() => BuildGet(ParamCurrentTemperature);

        public static byte[] GetTargetTemperature() => BuildGet(ParamCurrentSetPoint);

        public static byte[] BuildSetByte(ushort parameterId, byte value)
            => BuildSetParameter(parameterId, new[] { value });

        public static byte[] BuildSetFloat(ushort parameterId, float value)
        {
            var data = new byte[4];
            BinaryPrimitives.WriteSingleBigEndian(data, value);
            return BuildSetParameter(parameterId, data);
        }

        public static byte[] BuildGet(ushort parameterId)
        {
            var dataBlock = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(dataBlock.AsSpan(0, 2), parameterId);
            return Frame(ServiceGetParameters, dataBlock);
        }

        /// <summary>
        /// 응답 프레임에서 parameterId의 4바이트 float 값을 찾아 반환한다.
        /// 프레임이 깨졌거나 해당 파라미터가 없으면 FormatException을 던진다.
        /// </summary>
        public static float ParseFloat(byte[] frame, ushort parameterId)
        {
            if (frame is null || frame.Length < 7)
                throw new FormatException("SR-800N frame too short.");
            if (frame[0] != Sync)
                throw new FormatException($"SR-800N frame missing sync byte (got 0x{frame[0]:X2}).");

            int size = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2, 2));
            int checksumIndex = 4 + size - 1;
            if (checksumIndex >= frame.Length)
                throw new FormatException("SR-800N frame size exceeds buffer.");

            int i = 5;
            while (i + 4 <= checksumIndex)
            {
                ushort id = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(i, 2));
                int parameterSize = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(i + 2, 2));
                int parameterData = i + 4;
                if (id == parameterId)
                {
                    if (parameterSize != 4 || parameterData + 4 > frame.Length)
                        throw new FormatException($"SR-800N parameter 0x{parameterId:X4} is not a 4-byte float.");
                    return BinaryPrimitives.ReadSingleBigEndian(frame.AsSpan(parameterData, 4));
                }
                i = parameterData + parameterSize;
            }

            throw new FormatException($"SR-800N response has no parameter 0x{parameterId:X4}.");
        }

        /// <summary>파라미터 블록(id, 길이, 데이터)을 Set 서비스 프레임으로 감싼다.</summary>
        private static byte[] BuildSetParameter(ushort parameterId, byte[] parameterData)
        {
            var dataBlock = new byte[4 + parameterData.Length];
            BinaryPrimitives.WriteUInt16BigEndian(dataBlock.AsSpan(0, 2), parameterId);
            BinaryPrimitives.WriteUInt16BigEndian(dataBlock.AsSpan(2, 2), (ushort)parameterData.Length);
            parameterData.CopyTo(dataBlock, 4);
            return Frame(ServiceSetParameters, dataBlock);
        }

        /// <summary>공통 프레임 골격을 만든다. size는 service + 데이터 블록 + 체크섬 길이다.</summary>
        private static byte[] Frame(byte serviceCode, byte[] dataBlock)
        {
            int size = dataBlock.Length + 2;
            var frame = new byte[4 + size];
            frame[0] = Sync;
            frame[1] = AddressId;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)size);
            frame[4] = serviceCode;
            dataBlock.CopyTo(frame, 5);
            frame[^1] = Checksum(frame, frame.Length - 1);
            return frame;
        }

        /// <summary>바이트 합의 2의 보수 — 체크섬까지 더한 프레임 전체 합이 0이 되게 한다.</summary>
        private static byte Checksum(byte[] frame, int count)
        {
            int sum = 0;
            for (int i = 0; i < count; i++) sum += frame[i];
            return unchecked((byte)-sum);
        }
    }
}
