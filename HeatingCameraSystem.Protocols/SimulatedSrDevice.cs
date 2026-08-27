using System;
using System.Buffers.Binary;
using System.Diagnostics;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// SR-800N 흑체의 인메모리 시뮬레이터(<c>BlackBodySettings.Simulated</c>=true 경로).
    /// Set 프레임으로 받은 SV를 향해 PV를 초당 <c>SimulatedRampCelsiusPerSecond</c> ℃씩 선형 수렴시키고,
    /// Get 프레임에는 다음 <see cref="Read"/>가 돌려줄 응답 프레임을 만들어 둔다.
    /// </summary>
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
        /// <summary>직전 Get 요청에 대해 준비해 둔 응답 프레임을 반환한다.</summary>
        public byte[] Read() => _pendingResponse;
        public void Dispose() => Close();

        /// <summary>
        /// 요청 프레임을 파싱해 Set이면 파라미터를 적용하고 Get이면 응답을 준비한다.
        /// 형식이 어긋난 프레임은 조용히 무시한다.
        /// </summary>
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

        /// <summary>OperationMode와 SetPointAbsolute만 지원한다. SV 변경 시 현재 PV를 램프 기점으로 고정한다.</summary>
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

        /// <summary>마지막 SV 설정 시점부터 경과 시간 x 램프 속도만큼 PV를 SV 쪽으로 이동시킨 값.</summary>
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
