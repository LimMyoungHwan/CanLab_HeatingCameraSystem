using System;
using System.Diagnostics;
using System.Globalization;
using HeatingCameraSystem.Core.Config;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// 물리 장비 없이 SR-800R과 동일하게 동작하는 인메모리 흑체 (<see cref="ISrLink"/> 구현).
    /// <see cref="SrBlackBodyController"/>가 실장비와 똑같이 명령 문자열을 Write하고 응답을 ReadLine하므로
    /// 명령/응답/파싱 경로가 동일하다. 현재값(PV)은 SETTEMPERATURE로 정한 목표(SV)를 향해
    /// 램프 속도(℃/s)만큼 시간에 따라 수렴한다(흑체 열적 관성 재현).
    /// </summary>
    public sealed class SimulatedSrDevice : ISrLink
    {
        private readonly double _rampPerSec;
        private double _sv = 25.0;
        private double _pvAtSet = 25.0;
        private long _setAtTicks;
        private string _pendingReply = "*InvalidCommand*";
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
        public string ReadLine() => _pendingReply;
        public void Dispose() => Close();

        public void Write(string data)
        {
            string line = data.TrimEnd('\r', '\n').Trim();
            string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string cmd = parts.Length > 0 ? parts[0].ToUpperInvariant() : string.Empty;
            string? arg = parts.Length > 1 ? parts[1] : null;

            switch (cmd)
            {
                case "SETMODE":
                    _pendingReply = string.Empty;
                    break;
                case "SETTEMPERATURE":
                    if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    {
                        _pvAtSet = CurrentPv();
                        _sv = v;
                        _setAtTicks = Stopwatch.GetTimestamp();
                        _pendingReply = string.Empty;
                    }
                    else
                    {
                        _pendingReply = "*InvalidOperand*";
                    }
                    break;
                case "GETTEMPERATURE":
                    _pendingReply = Fmt(CurrentPv());
                    break;
                case "GETTARGETTEMPERATURE":
                    _pendingReply = Fmt(_sv);
                    break;
                default:
                    _pendingReply = "*InvalidCommand*";
                    break;
            }
        }

        private double CurrentPv()
        {
            double elapsedSec = (Stopwatch.GetTimestamp() - _setAtTicks) / (double)Stopwatch.Frequency;
            double maxStep = _rampPerSec * elapsedSec;
            double delta = _sv - _pvAtSet;
            if (Math.Abs(delta) <= maxStep) return _sv;
            return _pvAtSet + Math.Sign(delta) * maxStep;
        }

        private static string Fmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
