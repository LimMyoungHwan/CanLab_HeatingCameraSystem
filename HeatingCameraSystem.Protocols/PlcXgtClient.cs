using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using VagabondK.Protocols.Channels;
using VagabondK.Protocols.LSElectric;
using VagabondK.Protocols.LSElectric.FEnet;

namespace HeatingCameraSystem.Protocols
{
    /// <summary>
    /// LS XGT PLC — FEnet 전용 프로토콜(TCP, 기본 2004) 클라이언트.
    /// 통신은 VagabondK.Protocols.LSElectric(FEnetClient)에 위임하고,
    /// 상위 메서드는 논리 디바이스 토큰(PlcSettings)을 워드/비트 단위로 매핑한다.
    /// </summary>
    public class PlcXgtClient : IPlcController, IDisposable
    {
        private readonly PlcSettings _s;
        private readonly SemaphoreSlim _io = new(1, 1);
        private TcpChannel? _channel;
        private FEnetClient? _client;
        private volatile bool _isConnected;

        public bool IsConnected => _isConnected;

        public PlcXgtClient(PlcSettings? settings = null) => _s = settings ?? new PlcSettings();

        public async Task ConnectAsync(string ipAddress, int port = 2004)
        {
            if (_isConnected) return;
            await Task.Run(() =>
            {
                var channel = new TcpChannel(ipAddress, port);
                var client = new FEnetClient(channel)
                {
                    Timeout = 3000,
                    UseHexBitIndex = _s.UseHexBitIndex
                };
                _channel = channel;
                _client = client;
                _isConnected = true;
            });
        }

        public void Disconnect()
        {
            _isConnected = false;
            try { _client?.Dispose(); } catch { /* ignore */ }
            try { _channel?.Dispose(); } catch { /* ignore */ }
            _client = null;
            _channel = null;
        }

        // ── 챔버 온도/습도 ──
        public async Task StartChamberAsync()
        {
            await WriteBitAsync(_s.BitChamberRun, true);
            await WriteBitAsync(_s.BitTempStart, true);
        }

        public async Task StopChamberAsync()
        {
            await WriteBitAsync(_s.BitTempStart, false);
            await WriteBitAsync(_s.BitTempStop, true);
            await WriteBitAsync(_s.BitChamberRun, false);
        }

        public Task SetTargetTemperatureAsync(float temperature)
            => WriteWordAsync(_s.TempSv, ToScaled(temperature, 10));

        public async Task<float> GetCurrentTemperatureAsync()
            => FromScaled(await ReadWordAsync(_s.TempPv), 10);

        public Task SetTargetHumidityAsync(float humidity)
            => WriteWordAsync(_s.HumSv, ToScaled(humidity, 10));

        public async Task<float> GetCurrentHumidityAsync()
            => FromScaled(await ReadWordAsync(_s.HumPv), 10);

        public Task SetHumidityControlAsync(bool on)
            => WriteBitAsync(_s.BitHumidityControl, on);

        // ── 흑체 ──
        public Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
            => WriteWordAsync(blackBodyIndex == 0 ? _s.Bb1Sv : _s.Bb2Sv, ToScaled(temperature, 10));

        public async Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
            => FromScaled(await ReadWordAsync(blackBodyIndex == 0 ? _s.Bb1Pv : _s.Bb2Pv), 10);

        // ── 서보/모션 ──
        public Task MoveServoToPositionAsync(int positionIndex)
            => WriteBitAsync(PointMoveBit(positionIndex), true);

        public async Task<bool> IsServoAtPositionAsync(int positionIndex)
        {
            short current = await ReadWordAsync(_s.ServoCurrentPoint);
            bool busyX = await ReadBitAsync(_s.ServoXBusyBit);
            bool busyY = await ReadBitAsync(_s.ServoYBusyBit);
            return current == positionIndex && !busyX && !busyY;
        }

        public Task SetServoSpeedAsync(int percent)
            => WriteWordAsync(_s.ServoSpeedPercent, (short)Math.Clamp(percent, 1, 100));

        public Task JogAsync(ServoAxis axis, bool positive, bool on)
            => WriteBitAsync(JogBit(axis, positive), on);

        public Task HomeAsync(ServoAxis axis)
            => WriteBitAsync(axis == ServoAxis.X ? _s.BitHomeX : _s.BitHomeY, true);

        public async Task SetPointCoordinateAsync(int positionIndex, int x, int y)
        {
            var (xDev, yDev) = PointCoordDevices(positionIndex);
            await WriteWordAsync(xDev, (short)x);
            await WriteWordAsync(yDev, (short)y);
        }

        public async Task<(int X, int Y)> GetPointCoordinateAsync(int positionIndex)
        {
            var (xDev, yDev) = PointCoordDevices(positionIndex);
            short x = await ReadWordAsync(xDev);
            short y = await ReadWordAsync(yDev);
            return (x, y);
        }

        public async Task MoveToCoordinateAsync(int x, int y)
        {
            await WriteWordAsync(_s.ServoPointXBase, (short)x);
            await WriteWordAsync(_s.ServoPointYBase, (short)y);
            await WriteBitAsync(_s.ServoPointMoveBase, true);
        }

        // ── 수동 장비 ──
        public Task SetEquipmentAsync(PlcEquipment equipment, bool on)
            => WriteBitAsync(EquipmentDevice(equipment), on);

        public Task SetFanSpeedAsync(float hz)
            => WriteWordAsync(_s.FanSpeed, ToScaled(hz, 100));

        // ── 관리자 설정 ──
        public async Task WriteAdminSettingsAsync(PlcAdminSettings settings)
        {
            await WriteWordAsync(_s.AdminOverheatLimit, ToScaled(settings.OverheatLimit, 10));
            await WriteWordAsync(_s.AdminCoolerRoomBoundary, ToScaled(settings.CoolerRoomBoundary, 10));
            await WriteWordAsync(_s.AdminCooler2ndBoundary, ToScaled(settings.Cooler2ndBoundary, 10));
            await WriteWordAsync(_s.AdminCoolerDelay, (short)settings.CoolerDelayMinutes);
            await WriteWordAsync(_s.AdminBypassBoundary, ToScaled(settings.BypassBoundary, 10));
            await WriteWordAsync(_s.AdminMfcMinOutput, ToScaled(settings.MfcMinOutput, 10));
            await WriteWordAsync(_s.AdminMfcMaxOutput, ToScaled(settings.MfcMaxOutput, 10));
            await WriteWordAsync(_s.AdminPairGlassBoundary, ToScaled(settings.PairGlassBoundary, 10));
        }

        // ── 상태/에러 일괄 ──
        public async Task<PlcStatusSnapshot> ReadStatusAsync()
        {
            var s = new PlcStatusSnapshot
            {
                CurrentTemperature = FromScaled(await ReadWordAsync(_s.TempPv), 10),
                TargetTemperature = FromScaled(await ReadWordAsync(_s.TempSv), 10),
                CurrentHumidity = FromScaled(await ReadWordAsync(_s.HumPv), 10),
                TargetHumidity = FromScaled(await ReadWordAsync(_s.HumSv), 10),
                BlackBody1Pv = FromScaled(await ReadWordAsync(_s.Bb1Pv), 10),
                BlackBody1Sv = FromScaled(await ReadWordAsync(_s.Bb1Sv), 10),
                BlackBody2Pv = FromScaled(await ReadWordAsync(_s.Bb2Pv), 10),
                BlackBody2Sv = FromScaled(await ReadWordAsync(_s.Bb2Sv), 10),
                ServoXPosition = await ReadWordAsync(_s.ServoXPos),
                ServoYPosition = await ReadWordAsync(_s.ServoYPos),
                ServoXBusy = await ReadBitAsync(_s.ServoXBusyBit),
                ServoYBusy = await ReadBitAsync(_s.ServoYBusyBit),
                ServoXHomeComplete = await ReadBitAsync(_s.ServoXHomeBit),
                ServoYHomeComplete = await ReadBitAsync(_s.ServoYHomeBit),
                ServoXErrorCode = await ReadWordAsync(_s.ServoXErrorCode),
                ServoYErrorCode = await ReadWordAsync(_s.ServoYErrorCode),
                CurrentPoint = await ReadWordAsync(_s.ServoCurrentPoint),
                CurrentStep = await ReadWordAsync(_s.StepCurrent),
                TotalSteps = await ReadWordAsync(_s.StepTotal),
                FanSpeedHz = FromScaled(await ReadWordAsync(_s.FanSpeed), 100),
                GasFlow = FromScaled(await ReadWordAsync(_s.GasFlow), 10),
                Heater = await ReadBitAsync(_s.StatusHeater),
                Cooler1st = await ReadBitAsync(_s.StatusCooler1st),
                Cooler2nd = await ReadBitAsync(_s.StatusCooler2nd),
                CoolerRoom = await ReadBitAsync(_s.StatusCoolerRoom),
                CoolerRoomBypass = await ReadBitAsync(_s.StatusCoolerRoomBypass),
                DoorLamp = await ReadBitAsync(_s.StatusDoorLamp),
                PairGlass = await ReadBitAsync(_s.StatusPairGlass),
                Mcf = await ReadBitAsync(_s.StatusMcf),
                Blower1 = await ReadBitAsync(_s.StatusBlower1),
                Blower2 = await ReadBitAsync(_s.StatusBlower2)
            };

            s.ErrorBits = await ReadBitBlockAsync(_s.ErrorBitBase, s.ErrorBits.Length, hex: false);
            s.InputBits = await ReadBitBlockAsync(_s.InputBitBase, s.InputBits.Length, hex: true);
            s.OutputBits = await ReadBitBlockAsync(_s.OutputBitBase, s.OutputBits.Length, hex: true);

            s.Admin = new PlcAdminSettings
            {
                OverheatLimit = FromScaled(await ReadWordAsync(_s.AdminOverheatLimit), 10),
                CoolerRoomBoundary = FromScaled(await ReadWordAsync(_s.AdminCoolerRoomBoundary), 10),
                Cooler2ndBoundary = FromScaled(await ReadWordAsync(_s.AdminCooler2ndBoundary), 10),
                CoolerDelayMinutes = await ReadWordAsync(_s.AdminCoolerDelay),
                BypassBoundary = FromScaled(await ReadWordAsync(_s.AdminBypassBoundary), 10),
                MfcMinOutput = FromScaled(await ReadWordAsync(_s.AdminMfcMinOutput), 10),
                MfcMaxOutput = FromScaled(await ReadWordAsync(_s.AdminMfcMaxOutput), 10),
                PairGlassBoundary = FromScaled(await ReadWordAsync(_s.AdminPairGlassBoundary), 10)
            };

            return s;
        }

        public Task TriggerEmergencyStopAsync()
            => WriteBitAsync(_s.BitEmergencyStop, true);

        public void Dispose() => Disconnect();

        // ── 스케일/디바이스 토큰 매핑 ──

        private static short ToScaled(float value, int scale) => (short)Math.Round(value * scale);

        private static float FromScaled(short raw, int scale) => raw / (float)scale;

        private string PointMoveBit(int positionIndex) => IncDevice(_s.ServoPointMoveBase, positionIndex - 1);

        private (string X, string Y) PointCoordDevices(int positionIndex)
        {
            var (prefix, baseNum) = SplitDecimal(_s.ServoPointXBase);
            int x = baseNum + (positionIndex - 1) * _s.ServoPointStride;
            return ($"{prefix}{x}", $"{prefix}{x + 2}");
        }

        private string JogBit(ServoAxis axis, bool positive) => axis == ServoAxis.X
            ? (positive ? _s.BitJogXPlus : _s.BitJogXMinus)
            : (positive ? _s.BitJogYPlus : _s.BitJogYMinus);

        private string EquipmentDevice(PlcEquipment equipment) => equipment switch
        {
            PlcEquipment.Cooler1st => _s.EqCooler1st,
            PlcEquipment.Cooler2nd => _s.EqCooler2nd,
            PlcEquipment.CoolerRoom => _s.EqCoolerRoom,
            PlcEquipment.Blower1 => _s.EqBlower1,
            PlcEquipment.Blower2 => _s.EqBlower2,
            PlcEquipment.Chiller => _s.EqChiller,
            PlcEquipment.DoorLock => _s.EqDoorLock,
            PlcEquipment.Lighting => _s.EqLighting,
            PlcEquipment.PairGlass => _s.EqPairGlass,
            _ => throw new ArgumentOutOfRangeException(nameof(equipment))
        };

        private async Task<bool[]> ReadBitBlockAsync(string baseToken, int count, bool hex)
        {
            var arr = new bool[count];
            for (int i = 0; i < count; i++)
                arr[i] = await ReadBitAsync(IncDevice(baseToken, i, hex));
            return arr;
        }

        private static (string Prefix, int Number) SplitDecimal(string token)
        {
            int i = 0;
            while (i < token.Length && char.IsLetter(token[i])) i++;
            return (token.Substring(0, i), int.Parse(token.Substring(i)));
        }

        private static string IncDevice(string token, int offset, bool hex = false)
        {
            int i = 0;
            while (i < token.Length && char.IsLetter(token[i])) i++;
            string prefix = token.Substring(0, i);
            string numStr = token.Substring(i);
            if (hex)
            {
                long n = Convert.ToInt64(numStr, 16) + offset;
                return prefix + n.ToString("X" + numStr.Length);
            }
            return prefix + (long.Parse(numStr) + offset);
        }

        private static (string Area, string Suffix) SplitToken(string token)
        {
            int i = 0;
            while (i < token.Length && char.IsLetter(token[i])) i++;
            return (token.Substring(0, i), token.Substring(i));
        }

        private static bool TrySplitDotted(string token, out string wordToken, out int bit)
        {
            int dot = token.IndexOf('.');
            if (dot < 0) { wordToken = token; bit = 0; return false; }
            wordToken = token.Substring(0, dot);
            bit = int.Parse(token.Substring(dot + 1));
            return true;
        }

        private DeviceVariable ParseWord(string token)
        {
            var (area, suffix) = SplitToken(token);
            return DeviceVariable.Parse($"%{area}W{suffix}", _s.UseHexBitIndex);
        }

        private DeviceVariable ParseBit(string token)
        {
            var (area, suffix) = SplitToken(token);
            return DeviceVariable.Parse($"%{area}X{suffix}", _s.UseHexBitIndex);
        }

        // ── FEnet 프리미티브 (VagabondK 위임) ──
        // 비트-오브-워드('D2520.0')는 워드 읽기+마스크(쓰기는 read-modify-write)로 처리 — %DX CPU 편차 회피.

        private Task<short> ReadWordAsync(string token)
            => Query(client => client.Read(new[] { ParseWord(token) }).Values.First().WordValue);

        private Task WriteWordAsync(string token, short value)
            => Exec(client => client.Write(ParseWord(token), new DeviceValue(value)));

        private Task<bool> ReadBitAsync(string token)
            => Query(client =>
            {
                if (TrySplitDotted(token, out string wordToken, out int bit))
                {
                    short word = client.Read(new[] { ParseWord(wordToken) }).Values.First().WordValue;
                    return (word & (1 << bit)) != 0;
                }
                return client.Read(new[] { ParseBit(token) }).Values.First().BitValue;
            });

        private Task WriteBitAsync(string token, bool on)
            => Exec(client =>
            {
                if (TrySplitDotted(token, out string wordToken, out int bit))
                {
                    var wordVar = ParseWord(wordToken);
                    int word = (ushort)client.Read(new[] { wordVar }).Values.First().WordValue;
                    int updated = on ? (word | (1 << bit)) : (word & ~(1 << bit));
                    client.Write(wordVar, new DeviceValue((short)updated));
                }
                else
                {
                    client.Write(ParseBit(token), new DeviceValue(on));
                }
            });

        private async Task<T> Query<T>(Func<FEnetClient, T> action)
        {
            await _io.WaitAsync();
            try
            {
                var client = _client ?? throw new InvalidOperationException("Not connected to PLC.");
                return await Task.Run(() => action(client));
            }
            catch
            {
                _isConnected = false;
                throw;
            }
            finally { _io.Release(); }
        }

        private async Task Exec(Action<FEnetClient> action)
        {
            await _io.WaitAsync();
            try
            {
                var client = _client ?? throw new InvalidOperationException("Not connected to PLC.");
                await Task.Run(() => action(client));
            }
            catch
            {
                _isConnected = false;
                throw;
            }
            finally { _io.Release(); }
        }
    }
}
