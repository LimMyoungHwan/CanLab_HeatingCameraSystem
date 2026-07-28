using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// CI Systems SR-800R 흑체 직접-제어. 컨트롤러 1대 = 흑체 1개이므로 유닛마다 독립
    /// <see cref="ISrLink"/>(대수 = <see cref="BlackBodySettings.Units"/>.Count)를 연다.
    /// 프로토콜(매뉴얼 Chapter 6): 9600 8N1, Host→기기 EOM=CR, 기기→Host EOM=CR+LF,
    /// 메시지 간 최소 300ms. 연결 시 각 유닛을 Absolute 모드(SETMODE 1)로 맞춘다.
    /// <see cref="BlackBodySettings.Simulated"/>=true이면 실 시리얼 대신 <see cref="SimulatedSrDevice"/>를
    /// 사용해 물리 장비 없이 동일 경로로 동작한다.
    /// </summary>
    public sealed class SrBlackBodyController : IBlackBodyController
    {
        private sealed class Unit
        {
            public Unit(SerialSettings config, ISrLink link)
            {
                Config = config;
                Link = link;
            }

            public SerialSettings Config { get; }
            public ISrLink Link { get; }
            public readonly SemaphoreSlim Gate = new(1, 1);
            public long LastSendTicks;
        }

        private readonly BlackBodySettings _settings;
        private readonly Unit[] _units;
        private volatile bool _connected;

        public SrBlackBodyController(BlackBodySettings settings, Func<SerialSettings, ISrLink>? linkFactory = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Func<SerialSettings, ISrLink> factory = linkFactory ?? DefaultLink;
            var units = new List<Unit>();
            foreach (SerialSettings cfg in settings.Units)
                units.Add(new Unit(cfg, factory(cfg)));
            _units = units.ToArray();
        }

        private ISrLink DefaultLink(SerialSettings cfg)
            => _settings.Simulated
                ? new SimulatedSrDevice(_settings)
                : new SerialPortSrLink(cfg, _settings.ReadTimeoutMs);

        public int Count => _units.Length;
        public bool IsConnected => _connected;

        public async Task ConnectAsync()
        {
            foreach (Unit u in _units)
            {
                await u.Gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!u.Link.IsOpen) u.Link.Open();
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
                try { if (u.Link.IsOpen) u.Link.Close(); }
                catch (Exception ex) { Debug.WriteLine($"[SrBlackBody] close {u.Config.PortName}: {ex.Message}"); }
            }
            _connected = false;
        }

        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
            => WithUnit(blackBodyIndex, u => SendNoReplyLocked(u, SrProtocol.SetTemperature(celsius)));

        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryLocked(u, SrProtocol.GetTemperature()));

        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryLocked(u, SrProtocol.GetTargetTemperature()));

        public void Dispose()
        {
            foreach (Unit u in _units)
            {
                try { u.Link.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[SrBlackBody] dispose {u.Config.PortName}: {ex.Message}"); }
            }
            _connected = false;
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

        private static void EnsureOpen(Unit u)
        {
            if (!u.Link.IsOpen) u.Link.Open();
        }

        private Task SendNoReplyLocked(Unit u, string command)
            => Task.Run(() =>
            {
                if (!_settings.Simulated) RespectGap(u);
                u.Link.Write(command);
                u.LastSendTicks = Stopwatch.GetTimestamp();
            });

        private Task<float> QueryLocked(Unit u, string command)
            => Task.Run(() =>
            {
                if (!_settings.Simulated) RespectGap(u);
                u.Link.DiscardInBuffer();
                u.Link.Write(command);
                u.LastSendTicks = Stopwatch.GetTimestamp();
                return SrProtocol.ParseTemperature(u.Link.ReadLine());
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
