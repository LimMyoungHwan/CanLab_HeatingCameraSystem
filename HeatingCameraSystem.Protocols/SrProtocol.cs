using System;
using System.Buffers.Binary;

namespace HeatingCameraSystem.Protocols
{
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

        private static byte[] BuildSetParameter(ushort parameterId, byte[] parameterData)
        {
            var dataBlock = new byte[4 + parameterData.Length];
            BinaryPrimitives.WriteUInt16BigEndian(dataBlock.AsSpan(0, 2), parameterId);
            BinaryPrimitives.WriteUInt16BigEndian(dataBlock.AsSpan(2, 2), (ushort)parameterData.Length);
            parameterData.CopyTo(dataBlock, 4);
            return Frame(ServiceSetParameters, dataBlock);
        }

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

        private static byte Checksum(byte[] frame, int count)
        {
            int sum = 0;
            for (int i = 0; i < count; i++) sum += frame[i];
            return unchecked((byte)-sum);
        }
    }
}
