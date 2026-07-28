using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// CI Systems SR-800R 흑체 직접-제어 (RS-232). 컨트롤러 1대 = 흑체 1개이므로 유닛(포트)마다
    /// 독립 <see cref="SerialPort"/>를 연다(대수 = <see cref="BlackBodySettings.Units"/>.Count).
    /// 프로토콜(매뉴얼 Chapter 6): 9600 8N1, Host→기기 EOM=CR, 기기→Host EOM=CR+LF,
    /// 메시지 간 최소 300ms. 연결 시 각 유닛을 Absolute 모드(SETMODE 1)로 맞춘다.
    /// </summary>
    public sealed class SrBlackBodyController : IBlackBodyController
    {
        private sealed class Unit
        {
            public Unit(SerialSettings config) => Config = config;
            public SerialSettings Config { get; }
            public SerialPort? Port;
            public readonly SemaphoreSlim Gate = new(1, 1);
            public long LastSendTicks;
        }

        private readonly BlackBodySettings _settings;
        private readonly Unit[] _units;
        private volatile bool _connected;

        public SrBlackBodyController(BlackBodySettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            var units = new List<Unit>();
            foreach (SerialSettings cfg in settings.Units)
                units.Add(new Unit(cfg));
            _units = units.ToArray();
        }

        public int Count => _units.Length;
        public bool IsConnected => _connected;

        public async Task ConnectAsync()
        {
            foreach (Unit u in _units)
            {
                await u.Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    u.Port ??= OpenPort(u.Config);
                    await SendNoReplyLocked(u, SrProtocol.SetMode(1)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SrBlackBody] connect unit {u.Config.PortName} failed: {ex.Message}");
                }
                finally { u.Gate.Release(); }
            }
            _connected = true;
        }

        public void Disconnect()
        {
            foreach (Unit u in _units)
            {
                try { if (u.Port?.IsOpen == true) u.Port.Close(); }
                catch (Exception ex) { Debug.WriteLine($"[SrBlackBody] close {u.Config.PortName}: {ex.Message}"); }
                u.Port?.Dispose();
                u.Port = null;
            }
            _connected = false;
        }

        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
            => WithUnit(blackBodyIndex, u => SendNoReplyLocked(u, SrProtocol.SetTemperature(celsius)));

        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryLocked(u, SrProtocol.GetTemperature()));

        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryLocked(u, SrProtocol.GetTargetTemperature()));

        public void Dispose() => Disconnect();

        private SerialPort OpenPort(SerialSettings c)
        {
            var parity = Enum.TryParse<Parity>(c.Parity, true, out var p) ? p : Parity.None;
            var stop = Enum.TryParse<StopBits>(c.StopBits, true, out var sb) ? sb : StopBits.One;
            var port = new SerialPort(c.PortName, c.BaudRate, parity, c.DataBits, stop)
            {
                NewLine = "\r\n",
                ReadTimeout = _settings.ReadTimeoutMs,
                WriteTimeout = 2000
            };
            port.Open();
            return port;
        }

        private async Task WithUnit(int index, Func<Unit, Task> body)
        {
            Unit u = UnitAt(index);
            await u.Gate.WaitAsync().ConfigureAwait(false);
            try { EnsureOpen(u); await body(u).ConfigureAwait(false); }
            finally { u.Gate.Release(); }
        }

        private async Task<float> WithUnit(int index, Func<Unit, Task<float>> body)
        {
            Unit u = UnitAt(index);
            await u.Gate.WaitAsync().ConfigureAwait(false);
            try { EnsureOpen(u); return await body(u).ConfigureAwait(false); }
            finally { u.Gate.Release(); }
        }

        private Unit UnitAt(int index)
        {
            if (index < 0 || index >= _units.Length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Black body index {index} is out of range (0-{_units.Length - 1}).");
            return _units[index];
        }

        private void EnsureOpen(Unit u)
        {
            u.Port ??= OpenPort(u.Config);
            if (!u.Port.IsOpen) u.Port.Open();
        }

        private Task SendNoReplyLocked(Unit u, string command)
            => Task.Run(() =>
            {
                RespectGap(u);
                u.Port!.Write(command);
                u.LastSendTicks = Stopwatch.GetTimestamp();
            });

        private Task<float> QueryLocked(Unit u, string command)
            => Task.Run(() =>
            {
                RespectGap(u);
                u.Port!.DiscardInBuffer();
                u.Port.Write(command);
                u.LastSendTicks = Stopwatch.GetTimestamp();
                return SrProtocol.ParseTemperature(u.Port.ReadLine());
            });

        private void RespectGap(Unit u)
        {
            if (u.LastSendTicks == 0) return;
            double elapsedMs = (Stopwatch.GetTimestamp() - u.LastSendTicks) * 1000.0 / Stopwatch.Frequency;
            int wait = _settings.InterMessageDelayMs - (int)elapsedMs;
            if (wait > 0) Thread.Sleep(wait);
        }
    }
}
