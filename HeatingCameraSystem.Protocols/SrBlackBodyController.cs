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
    /// CI Systems SR-800N 흑체 직접-제어. 컨트롤러 1대 = 흑체 1개이므로 유닛마다 독립
    /// <see cref="ISrLink"/>(대수 = <see cref="BlackBodySettings.Units"/>.Count)를 연다.
    /// 프로토콜(SR-800N 통신 규격 6057060): RS-232 115200 8N1, 바이너리 VIP 프레임
    /// (0xAA sync + 파라미터 코드 + IEEE754 big-endian + 체크섬). 연결 시 각 유닛을
    /// Absolute 모드(OperationMode 0x07F0=1)로 맞춘다.
    /// <see cref="BlackBodySettings.Simulated"/>=true이면 실 시리얼 대신 <see cref="SimulatedSrDevice"/>를
    /// 사용해 물리 장비 없이 동일 경로로 동작한다.
    /// </summary>
    public sealed class SrBlackBodyController : IBlackBodyController
    {
        private sealed class Unit
        {
            public Unit(BlackBodyUnitSettings config, ISrLink link)
            {
                Config = config;
                Link = link;
            }

            public BlackBodyUnitSettings Config { get; }
            public ISrLink Link { get; }
            public readonly SemaphoreSlim Gate = new(1, 1);
            public long LastSendTicks;
        }

        private readonly BlackBodySettings _settings;
        private readonly IPlcController? _plc;
        private readonly Unit[] _units;
        private volatile bool _connected;

        public SrBlackBodyController(
            BlackBodySettings settings,
            Func<BlackBodyUnitSettings, ISrLink>? linkFactory = null,
            IPlcController? plc = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _plc = plc;
            Func<BlackBodyUnitSettings, ISrLink> factory = linkFactory ?? DefaultLink;
            var units = new List<Unit>();
            foreach (BlackBodyUnitSettings cfg in settings.Units)
                units.Add(new Unit(cfg, factory(cfg)));
            _units = units.ToArray();
        }

        private ISrLink DefaultLink(BlackBodyUnitSettings cfg)
            => _settings.Simulated
                ? new SimulatedSrDevice(_settings)
                : cfg.ConnectionType == BlackBodyConnectionType.Ip
                    ? new UdpSrLink(cfg, _settings.ReadTimeoutMs)
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
                    Debug.WriteLine($"[SrBlackBody] connect unit {Describe(u.Config)} failed: {ex.Message}");
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
                catch (Exception ex) { Debug.WriteLine($"[SrBlackBody] close {Describe(u.Config)}: {ex.Message}"); }
            }
            _connected = false;
        }

        public Task SetTemperatureAsync(int blackBodyIndex, float celsius)
            => _plc == null
                ? WithUnit(blackBodyIndex, u => SendNoReplyLocked(u, SrProtocol.SetTemperature(celsius)))
                : Task.WhenAll(
                    WithUnit(blackBodyIndex, u => SendNoReplyLocked(u, SrProtocol.SetTemperature(celsius))),
                    _plc.SetBlackBodyTemperatureAsync(blackBodyIndex, celsius));

        public Task<float> GetCurrentTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryFloatLocked(u, SrProtocol.GetTemperature(), SrProtocol.ParamCurrentTemperature));

        public Task<float> GetTargetTemperatureAsync(int blackBodyIndex)
            => WithUnit(blackBodyIndex, u => QueryFloatLocked(u, SrProtocol.GetTargetTemperature(), SrProtocol.ParamCurrentSetPoint));

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

        private Task SendNoReplyLocked(Unit u, byte[] command)
            => Task.Run(() =>
            {
                if (!_settings.Simulated) RespectGap(u);
                u.Link.Write(command);
                u.LastSendTicks = Stopwatch.GetTimestamp();
            });

        private Task<float> QueryFloatLocked(Unit u, byte[] request, ushort parameterId)
            => Task.Run(() =>
            {
                if (!_settings.Simulated) RespectGap(u);
                u.Link.DiscardInBuffer();
                u.Link.Write(request);
                u.LastSendTicks = Stopwatch.GetTimestamp();
                return SrProtocol.ParseFloat(u.Link.Read(), parameterId);
            });

        private void RespectGap(Unit u)
        {
            if (u.LastSendTicks == 0) return;
            double elapsedMs = (Stopwatch.GetTimestamp() - u.LastSendTicks) * 1000.0 / Stopwatch.Frequency;
            int wait = _settings.InterMessageDelayMs - (int)elapsedMs;
            if (wait > 0) Thread.Sleep(wait);
        }

        private static string Describe(BlackBodyUnitSettings config)
            => config.ConnectionType == BlackBodyConnectionType.Ip
                ? $"{config.IpAddress}:{config.Port}"
                : config.PortName;
    }
}
