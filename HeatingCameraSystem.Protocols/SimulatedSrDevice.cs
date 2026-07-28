using System;
using System.Buffers.Binary;
using System.Diagnostics;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    public sealed class SimulatedSrDevice : ISrLink
    {
        private readonly double _rampPerSec;
        private double _sv = 25.0;
        private double _pvAtSet = 25.0;
        private long _setAtTicks;
        private byte _mode = SrProtocol.ModeAbsolute;
        private byte[] _pendingResponse = Array.Empty<byte>();
        private bool _open;

        public SimulatedSrDevice(BlackBodySettings settings)
        {
            _rampPerSec = settings.SimulatedRampCelsiusPerSecond > 0 ? settings.SimulatedRampCelsiusPerSecond : 5.0;
            _setAtTicks = Stopwatch.GetTimestamp();
        }

        public bool IsOpen => _open;
        public void Open() => _open = true;
        public void Close() => _open = false;
        public void DiscardInBuffer() { }
        public byte[] Read() => _pendingResponse;
        public void Dispose() => Close();

        public void Write(byte[] data)
        {
            if (data is null || data.Length < 7 || data[0] != SrProtocol.Sync) return;

            byte service = data[4];
            int size = (data[2] << 8) | data[3];
            int checksumIndex = 4 + size - 1;
            if (checksumIndex >= data.Length) return;

            int i = 5;
            while (i + 4 <= checksumIndex)
            {
                ushort id = (ushort)((data[i] << 8) | data[i + 1]);
                int parameterSize = (data[i + 2] << 8) | data[i + 3];
                int parameterData = i + 4;

                if (service == SrProtocol.ServiceSetParameters)
                    ApplySet(id, data, parameterData, parameterSize);
                else if (service == SrProtocol.ServiceGetParameters)
                    _pendingResponse = BuildGetResponse(id);

                i = parameterData + parameterSize;
            }
        }

        private void ApplySet(ushort id, byte[] data, int offset, int size)
        {
            switch (id)
            {
                case SrProtocol.ParamOperationMode when size >= 1:
                    _mode = data[offset];
                    break;
                case SrProtocol.ParamSetPointAbsolute when size >= 4:
                    _pvAtSet = CurrentPv();
                    _sv = BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset, 4));
                    _setAtTicks = Stopwatch.GetTimestamp();
                    break;
            }
        }

        private byte[] BuildGetResponse(ushort id) => id switch
        {
            SrProtocol.ParamCurrentTemperature => SrProtocol.BuildSetFloat(id, (float)CurrentPv()),
            SrProtocol.ParamCurrentSetPoint => SrProtocol.BuildSetFloat(id, (float)_sv),
            SrProtocol.ParamOperationMode => SrProtocol.BuildSetByte(id, _mode),
            _ => Array.Empty<byte>()
        };

        private double CurrentPv()
        {
            double elapsedSec = (Stopwatch.GetTimestamp() - _setAtTicks) / (double)Stopwatch.Frequency;
            double maxStep = _rampPerSec * elapsedSec;
            double delta = _sv - _pvAtSet;
            if (Math.Abs(delta) <= maxStep) return _sv;
            return _pvAtSet + Math.Sign(delta) * maxStep;
        }
    }
}
