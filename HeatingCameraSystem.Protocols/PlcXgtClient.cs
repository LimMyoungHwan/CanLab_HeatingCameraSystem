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

        /// <summary>TCP 채널과 FEnetClient를 생성한다. 이미 연결돼 있으면 아무것도 하지 않는다.</summary>
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
            try { _client?.Dispose(); } catch { /* 무시 */ }
            try { _channel?.Dispose(); } catch { /* 무시 */ }
            _client = null;
            _channel = null;
        }

        // ── 챔버 온도/습도 ──
        public async Task StartChamberAsync()
        {
            await WriteBitAsync(_s.BitChamberRun, true);
            await WriteBitAsync(_s.BitTempStart, true);
            await WriteBitAsync(_s.BitTempStopLamp, false);
            await WriteBitAsync(_s.BitTempStop, false);
        }

        public async Task StopChamberAsync()
        {
            await WriteBitAsync(_s.BitTempStart, false);
            await WriteBitAsync(_s.BitTempStopLamp, true);
            await WriteBitAsync(_s.BitTempStop, true);
            await WriteBitAsync(_s.BitChamberRun, false);
        }

        /// <summary>목표 온도(℃)를 0.1℃ 단위 워드(x10)로 기록한다.</summary>
        public Task SetTargetTemperatureAsync(float temperature)
            => WriteWordAsync(_s.TempTarget, ToScaled(temperature, 10));

        /// <summary>제어(SV) 온도(℃)를 0.1℃ 단위 워드(x10)로 기록한다.</summary>
        public Task SetControlTemperatureAsync(float temperature)
            => WriteWordAsync(_s.TempSv, ToScaled(temperature, 10));

        public async Task<float> GetCurrentTemperatureAsync()
            => FromScaled(await ReadWordAsync(_s.TempPv), 10);

        /// <summary>목표 습도를 0.1 단위 워드(x10)로 기록한다.</summary>
        public Task SetTargetHumidityAsync(float humidity)
            => WriteWordAsync(_s.HumSv, ToScaled(humidity, 10));

        public async Task<float> GetCurrentHumidityAsync()
            => FromScaled(await ReadWordAsync(_s.HumPv), 10);

        public Task SetHumidityControlAsync(bool on)
            => WriteWordAsync(_s.BitHumidityControl, 1);

        // ── 흑체 ──
        /// <summary>흑체 SV(℃)를 0.01℃ 단위 워드(x100)로 기록한다. blackBodyIndex 0 → Bb1, 그 외 → Bb2.</summary>
        public Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
            => WriteWordAsync(blackBodyIndex == 0 ? _s.Bb1Sv : _s.Bb2Sv, ToScaled(temperature, 100));

        public async Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
            => FromScaled(await ReadWordAsync(blackBodyIndex == 0 ? _s.Bb1Pv : _s.Bb2Pv), 100);

        public async Task WriteBlackBodyTemperaturesAsync(int blackBodyIndex, float currentTemperature, float targetTemperature)
        {
            await WriteWordAsync(blackBodyIndex == 0 ? _s.Bb1Pv : _s.Bb2Pv, ToScaled(currentTemperature, 100));
            await WriteWordAsync(blackBodyIndex == 0 ? _s.Bb1Sv : _s.Bb2Sv, ToScaled(targetTemperature, 100));
        }

        // ── 서보/모션 ──
        /// <summary>포지션 이동 트리거 비트를 올린다(P 비트 모멘터리). positionIndex는 1부터 시작한다.</summary>
        public Task MoveServoToPositionAsync(int positionIndex)
            => WriteBitAsync(PointMoveBit(positionIndex), true);

        /// <summary>현재 포인트 워드가 positionIndex와 같고 X/Y 축 모두 Busy가 아닐 때만 true다.</summary>
        public async Task<bool> IsServoAtPositionAsync(int positionIndex)
        {
            short current = await ReadWordAsync(_s.ServoCurrentPoint);
            bool busyX = await ReadBitAsync(_s.ServoXBusyBit);
            bool busyY = await ReadBitAsync(_s.ServoYBusyBit);
            return current == positionIndex && !busyX && !busyY;
        }

        public Task SetServoSpeedAsync(int percent)
            => WriteWordAsync(_s.ServoSpeedPercent, (short)Math.Clamp(percent, 1, 100));

        // JOG는 누름/뗌 유지 동작이므로 P 비트라도 모멘터리 펄스를 적용하지 않는다.
        public Task JogAsync(ServoAxis axis, bool positive, bool on)
            => WriteBitRawAsync(JogBit(axis, positive), on);

        public Task HomeAsync(ServoAxis axis)
            => WriteBitAsync(axis == ServoAxis.X ? _s.BitHomeX : _s.BitHomeY, true);

        // 좌표 워드는 0.1mm 단위 정수(x10 스케일). 외부 API는 mm 단위 float.
        public async Task SetPointCoordinateAsync(int positionIndex, float x, float y)
        {
            var (xDev, yDev) = PointCoordDevices(positionIndex);
            await WriteWordAsync(xDev, ToScaled(x, 10));
            await WriteWordAsync(yDev, ToScaled(y, 10));
        }

        public async Task<(float X, float Y)> GetPointCoordinateAsync(int positionIndex)
        {
            var (xDev, yDev) = PointCoordDevices(positionIndex);
            short x = await ReadWordAsync(xDev);
            short y = await ReadWordAsync(yDev);
            return (FromScaled(x, 10), FromScaled(y, 10));
        }

        public async Task MoveToCoordinateAsync(float x, float y)
        {
            await WriteWordAsync(_s.ServoPointXBase, ToScaled(x, 10));
            await WriteWordAsync(_s.ServoPointYBase, ToScaled(y, 10));
            // 서보가 목표 워드를 래치한 뒤 이동 트리거가 상승엣지로 잡히도록 지연.
            if (_s.CoordinateMoveDelayMs > 0) await Task.Delay(_s.CoordinateMoveDelayMs);
            await WriteBitAsync(_s.ServoPointMoveBase, true);
        }

        // ── 수동 장비 ──
        public Task SetEquipmentAsync(PlcEquipment equipment, bool on)
            => WriteBitAsync(EquipmentDevice(equipment), on);

        /// <summary>팬 속도(Hz)를 0.01Hz 단위 워드(x100)로 기록한다.</summary>
        public Task SetFanSpeedAsync(float hz)
            => WriteWordAsync(_s.FanSpeed, ToScaled(hz, 100));

        // ── 관리자 설정 ──
        /// <summary>관리자 설정을 일괄 기록한다. 지연(분) 워드만 원값이고 나머지는 전부 0.1 단위(x10)다.</summary>
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
        /// <summary>
        /// 상태 화면이 1초 주기로 호출하는 일괄 판독. 항목마다 개별 FEnet 요청을 순차 수행하므로
        /// 항목을 늘리면 그만큼 폴링 한 사이클이 길어진다.
        /// </summary>
        /// <summary>
        /// 전체 상태를 두 번의 일괄 판독(워드 배치 / 비트 배치)으로 채운다. 변수 하나당 한 요청을
        /// 보내면 126왕복이 되어 XGB 스캔 시간에서 1초를 넘고, 그러면 1초 주기인
        /// <c>PlcStatusService</c>의 재진입 가드가 틱을 건너뛰어 화면이 2초마다 갱신된다.
        /// <para>
        /// 비트-오브-워드('D60.1')는 워드 배치에 실어 마스킹하므로 같은 워드를 공유하는 비트가
        /// 왕복 하나로 합쳐진다(D60.1~D60.9 → D60 한 번). 개별읽기 헤더의 데이터 타입은 요청당
        /// 하나뿐이라 워드와 순수 비트는 섞지 않는다.
        /// </para>
        /// </summary>
        public async Task<PlcStatusSnapshot> ReadStatusAsync()
        {
            var s = new PlcStatusSnapshot();

            string[] errorBitTokens = BitBlockTokens(_s.ErrorBitBase, s.ErrorBits.Length, hex: false);
            string[] inputBitTokens = BitBlockTokens(_s.InputBitBase, s.InputBits.Length, hex: true);
            string[] outputBitTokens = BitBlockTokens(_s.OutputBitBase, s.OutputBits.Length, hex: true);

            string[] scalarWordTokens =
            {
                _s.TempPv, _s.TempTarget, _s.HumPv, _s.HumSv,
                _s.Bb1Pv, _s.Bb1Sv, _s.Bb2Pv, _s.Bb2Sv,
                _s.ServoXPos, _s.ServoYPos, _s.ServoXErrorCode, _s.ServoYErrorCode,
                _s.ServoCurrentPoint, _s.StepCurrent, _s.StepTotal, _s.FanSpeed, _s.GasFlow,
                _s.AdminOverheatLimit, _s.AdminCoolerRoomBoundary, _s.AdminCooler2ndBoundary,
                _s.AdminCoolerDelay, _s.AdminBypassBoundary, _s.AdminMfcMinOutput,
                _s.AdminMfcMaxOutput, _s.AdminPairGlassBoundary
            };

            string[] bitTokens = new[]
            {
                _s.ServoXBusyBit, _s.ServoYBusyBit, _s.ServoXHomeBit, _s.ServoYHomeBit,
                _s.StatusHeater, _s.StatusCooler1st, _s.StatusCooler2nd, _s.StatusCoolerRoom,
                _s.StatusCoolerRoomBypass, _s.StatusDoorLamp, _s.StatusPairGlass, _s.StatusMcf,
                _s.StatusBlower1, _s.StatusBlower2, _s.StatusChiller, _s.StatusDoorLock,
                _s.StatusLighting
            }
            .Concat(errorBitTokens).Concat(inputBitTokens).Concat(outputBitTokens).ToArray();

            var words = await ReadBatchAsync(scalarWordTokens.Concat(DottedWordTokens(bitTokens)).Select(ParseWord));
            var bits = await ReadBatchAsync(PureBitTokens(bitTokens).Select(ParseBit));

            short Raw(string token) => Lookup(words, ParseWord(token), token).WordValue;
            float Word(string token, int scale) => FromScaled(Raw(token), scale);
            bool Bit(string token) => TrySplitDotted(token, out string wordToken, out int bit)
                ? (Raw(wordToken) & (1 << bit)) != 0
                : Lookup(bits, ParseBit(token), token).BitValue;
            bool[] Block(string[] tokens) => Array.ConvertAll(tokens, Bit);

            s.CurrentTemperature = Word(_s.TempPv, 10);
            s.TargetTemperature = Word(_s.TempTarget, 10);
            s.CurrentHumidity = Word(_s.HumPv, 10);
            s.TargetHumidity = Word(_s.HumSv, 10);
            s.BlackBody1Pv = Word(_s.Bb1Pv, 100);
            s.BlackBody1Sv = Word(_s.Bb1Sv, 100);
            s.BlackBody2Pv = Word(_s.Bb2Pv, 100);
            s.BlackBody2Sv = Word(_s.Bb2Sv, 100);
            s.ServoXPosition = Word(_s.ServoXPos, 10);
            s.ServoYPosition = Word(_s.ServoYPos, 10);
            s.ServoXBusy = Bit(_s.ServoXBusyBit);
            s.ServoYBusy = Bit(_s.ServoYBusyBit);
            s.ServoXHomeComplete = Bit(_s.ServoXHomeBit);
            s.ServoYHomeComplete = Bit(_s.ServoYHomeBit);
            s.ServoXErrorCode = Raw(_s.ServoXErrorCode);
            s.ServoYErrorCode = Raw(_s.ServoYErrorCode);
            s.CurrentPoint = Raw(_s.ServoCurrentPoint);
            s.CurrentStep = Raw(_s.StepCurrent);
            s.TotalSteps = Raw(_s.StepTotal);
            s.FanSpeedHz = Word(_s.FanSpeed, 100);
            s.GasFlow = Word(_s.GasFlow, 10);
            s.Heater = Bit(_s.StatusHeater);
            s.Cooler1st = Bit(_s.StatusCooler1st);
            s.Cooler2nd = Bit(_s.StatusCooler2nd);
            s.CoolerRoom = Bit(_s.StatusCoolerRoom);
            s.CoolerRoomBypass = Bit(_s.StatusCoolerRoomBypass);
            s.DoorLamp = Bit(_s.StatusDoorLamp);
            s.PairGlass = Bit(_s.StatusPairGlass);
            s.Mcf = Bit(_s.StatusMcf);
            s.Blower1 = Bit(_s.StatusBlower1);
            s.Blower2 = Bit(_s.StatusBlower2);
            s.Chiller = Bit(_s.StatusChiller);
            s.DoorLock = Bit(_s.StatusDoorLock);
            s.Lighting = Bit(_s.StatusLighting);

            s.ErrorBits = Block(errorBitTokens);
            s.InputBits = Block(inputBitTokens);
            s.OutputBits = Block(outputBitTokens);

            s.Admin = new PlcAdminSettings
            {
                OverheatLimit = Word(_s.AdminOverheatLimit, 10),
                CoolerRoomBoundary = Word(_s.AdminCoolerRoomBoundary, 10),
                Cooler2ndBoundary = Word(_s.AdminCooler2ndBoundary, 10),
                CoolerDelayMinutes = Raw(_s.AdminCoolerDelay),
                BypassBoundary = Word(_s.AdminBypassBoundary, 10),
                MfcMinOutput = Word(_s.AdminMfcMinOutput, 10),
                MfcMaxOutput = Word(_s.AdminMfcMaxOutput, 10),
                PairGlassBoundary = Word(_s.AdminPairGlassBoundary, 10)
            };

            return s;
        }

        public async Task TriggerEmergencyStopAsync()
        {
            await WriteBitRawAsync(_s.BitEmergencyStop, true);
            await Task.Delay(100);
            await WriteBitRawAsync(_s.BitEmergencyStop, false);
        }

        // P 비트 → WriteBitAsync가 ON 후 PulseHoldMs 뒤 OFF까지 처리.
        public Task ResetErrorAsync() => WriteBitAsync(_s.BitErrorReset, true);

        public Task BuzzerOffAsync() => WriteBitAsync(_s.BitBuzzerOff, true);

        public void Dispose() => Disconnect();

        // ── 스케일/디바이스 토큰 매핑 ──

        private static short ToScaled(float value, int scale) => (short)Math.Round(value * scale);

        private static float FromScaled(short raw, int scale) => raw / (float)scale;

        /// <summary>positionIndex(1부터)를 이동 트리거 비트 토큰으로 변환한다(base + index - 1).</summary>
        private string PointMoveBit(int positionIndex) => IncDevice(_s.ServoPointMoveBase, positionIndex - 1);

        /// <summary>positionIndex(1부터)의 X/Y 좌표 워드 토큰. 포인트당 ServoPointStride 간격이고 Y는 X + 2 워드다.</summary>
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

        /// <summary>base 토큰부터 연속 count개 비트 토큰을 만든다. hex=true면 주소 증가를 16진수로 계산한다.</summary>
        private static string[] BitBlockTokens(string baseToken, int count, bool hex)
        {
            var tokens = new string[count];
            for (int i = 0; i < count; i++)
                tokens[i] = IncDevice(baseToken, i, hex);
            return tokens;
        }

        private static IEnumerable<string> DottedWordTokens(IEnumerable<string> bitTokens)
        {
            foreach (string token in bitTokens)
                if (TrySplitDotted(token, out string wordToken, out _))
                    yield return wordToken;
        }

        private static IEnumerable<string> PureBitTokens(IEnumerable<string> bitTokens)
        {
            foreach (string token in bitTokens)
                if (!TrySplitDotted(token, out _, out _))
                    yield return token;
        }

        /// <summary>
        /// 중복을 제거한 변수들을 <see cref="PlcSettings.ReadBatchSize"/>개씩 묶어 판독한다.
        /// 모든 변수는 같은 데이터 타입이어야 한다 — 개별읽기 요청 헤더가 타입 하나만 싣는다.
        /// </summary>
        private async Task<Dictionary<DeviceVariable, DeviceValue>> ReadBatchAsync(IEnumerable<DeviceVariable> variables)
        {
            var merged = new Dictionary<DeviceVariable, DeviceValue>();
            int batchSize = Math.Max(1, _s.ReadBatchSize);

            foreach (DeviceVariable[] chunk in variables.Distinct().Chunk(batchSize))
            {
                IReadOnlyDictionary<DeviceVariable, DeviceValue> values = await Query(client => client.Read(chunk));
                foreach (var pair in values)
                    merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        private static DeviceValue Lookup(Dictionary<DeviceVariable, DeviceValue> values, DeviceVariable variable, string token)
            => values.TryGetValue(variable, out DeviceValue value)
                ? value
                : throw new InvalidOperationException($"PLC status read returned no value for '{token}' ({variable}).");

        private static (string Prefix, int Number) SplitDecimal(string token)
        {
            int i = 0;
            while (i < token.Length && char.IsLetter(token[i])) i++;
            return (token.Substring(0, i), int.Parse(token.Substring(i)));
        }

        /// <summary>디바이스 토큰의 숫자부를 offset만큼 올린다. hex=true면 자릿수를 유지한 16진수 연산이다.</summary>
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

        /// <summary>'D2520.0' 형태를 워드 토큰과 비트 번호로 분리한다. 점이 없으면 false.</summary>
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

        // P 영역 트리거 비트는 모멘터리: ON 쓰고 PulseHoldMs 대기 후 OFF로 되돌린다(PLC가 상승엣지로 래치).
        // OFF 쓰기와 비-P 디바이스는 그대로 통과.
        private async Task WriteBitAsync(string token, bool on)
        {
            await WriteBitRawAsync(token, on);
            if (!on || !IsPulseBit(token)) return;
            await Task.Delay(_s.PulseHoldMs);
            await WriteBitRawAsync(token, false);
        }

        private static bool IsPulseBit(string token)
            => token.Length > 0 && char.ToUpperInvariant(token[0]) == 'P';

        private Task WriteBitRawAsync(string token, bool on)
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

        /// <summary>
        /// _io 세마포어로 직렬화해 실행한다. 실패하면 연결 끊김으로 표시하고 예외를 그대로 던진다
        /// — 재연결은 ConnectionMonitorService 몫이다.
        /// </summary>
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

        /// <summary>쓰기용 실행 경로. 직렬화와 실패 처리는 <see cref="Query{T}"/>와 동일하다.</summary>
        private async Task Exec(Action<FEnetClient> action)
        {
            await _io.WaitAsync();
            try
            {
                var client = _client ?? throw new InvalidOperationException("Not connected to PLC.");
                await Task.Run(() => action(client));
                if (_s.WriteGapMs > 0) await Task.Delay(_s.WriteGapMs);
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
