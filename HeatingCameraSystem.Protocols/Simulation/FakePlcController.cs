using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
    /// <summary>
    /// 하드웨어 없이 동작하는 가짜 PLC 컨트롤러. SimulationMode=true 시
    /// PlcXgtClient(LS XGT FEnet, TCP 2004) 대신 사용한다.
    /// 네트워크 I/O 없이 모든 명령이 즉시 성공하고 상태는 인메모리로만 유지된다:
    /// 습도·흑체는 타겟 설정 시 현재값이 즉시 스냅하고, 서보 이동은 즉시 도착 처리된다
    /// (챔버 온도만 예외 — 타겟과 현재가 분리되어 RecipeEngine의 램프 로직을 검증할 수 있다).
    /// 연결 전에 명령을 호출하면 InvalidOperationException을 던진다.
    /// </summary>
    public class FakePlcController : IPlcController, IDisposable
    {
        private readonly object _gate = new();
        private bool _isConnected;
        private float _currentTemp;
        private float _targetTemp;
        private float _currentHum;
        private float _targetHum;
        private bool _humidityOn;
        private int _currentPoint = -1;
        private float _servoX;
        private float _servoY;
        private int _servoSpeedPercent = 100;
        private float _fanHz;
        private readonly ConcurrentDictionary<int, float> _bbCurrent = new();
        private readonly ConcurrentDictionary<int, (float X, float Y)> _pointCoords = new();
        private readonly ConcurrentDictionary<PlcEquipment, bool> _equipment = new();
        private PlcAdminSettings _admin = new();

        public bool IsConnected
        {
            get { lock (_gate) return _isConnected; }
        }

        /// <summary>실제 접속 없이 즉시 연결 상태가 되고, 온도 25.0℃·습도 50.0%RH로 초기화한다.</summary>
        public Task ConnectAsync(string ipAddress, int port = 2004)
        {
            lock (_gate)
            {
                _isConnected = true;
                _currentTemp = 25.0f;
                _targetTemp = 25.0f;
                _currentHum = 50.0f;
                _targetHum = 50.0f;
            }
            Log($"ConnectAsync({ipAddress}:{port}) -> OK (simulated)");
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            lock (_gate) _isConnected = false;
            Log("Disconnect() -> OK (simulated)");
        }

        /// <summary>로그만 남긴다. 내부 상태 변화는 없다.</summary>
        public Task StartChamberAsync()
        {
            EnsureConnected();
            Log("StartChamberAsync() -> RUN");
            return Task.CompletedTask;
        }

        /// <summary>로그만 남긴다. 내부 상태 변화는 없다.</summary>
        public Task StopChamberAsync()
        {
            EnsureConnected();
            Log("StopChamberAsync() -> STOP");
            return Task.CompletedTask;
        }

        /// <summary>타겟(SV)만 기록한다. 현재 온도(PV)는 변하지 않는다 — 램프는 RecipeEngine이 <see cref="SetControlTemperatureAsync"/>로 밀어 올린다.</summary>
        public Task SetTargetTemperatureAsync(float temperature)
        {
            EnsureConnected();
            lock (_gate) _targetTemp = temperature;
            Log($"SetTargetTemperatureAsync({temperature})");
            return Task.CompletedTask;
        }

        /// <summary>현재 온도(PV)가 제어 온도로 즉시 스냅한다. 실 챔버의 승온 지연은 시뮬레이션하지 않는다.</summary>
        public Task SetControlTemperatureAsync(float temperature)
        {
            EnsureConnected();
            lock (_gate) _currentTemp = temperature;
            Log($"SetControlTemperatureAsync({temperature}) -> current snaps to control");
            return Task.CompletedTask;
        }

        public Task<float> GetCurrentTemperatureAsync()
        {
            EnsureConnected();
            float v;
            lock (_gate) v = _currentTemp;
            return Task.FromResult(v);
        }

        /// <summary>목표 설정과 동시에 현재 습도가 목표로 즉시 스냅한다.</summary>
        public Task SetTargetHumidityAsync(float humidity)
        {
            EnsureConnected();
            lock (_gate) { _targetHum = humidity; _currentHum = humidity; }
            Log($"SetTargetHumidityAsync({humidity}) -> current snaps to target");
            return Task.CompletedTask;
        }

        public Task<float> GetCurrentHumidityAsync()
        {
            EnsureConnected();
            float v;
            lock (_gate) v = _currentHum;
            return Task.FromResult(v);
        }

        public Task SetHumidityControlAsync(bool on)
        {
            EnsureConnected();
            lock (_gate) _humidityOn = on;
            Log($"SetHumidityControlAsync({on})");
            return Task.CompletedTask;
        }

        /// <summary>흑체 현재 온도(PV)가 타겟으로 즉시 스냅한다.</summary>
        public Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
        {
            EnsureConnected();
            _bbCurrent[blackBodyIndex] = temperature;
            Log($"SetBlackBodyTemperatureAsync(BB{blackBodyIndex}, {temperature}) -> snaps");
            return Task.CompletedTask;
        }

        /// <summary>한 번도 설정하지 않은 흑체 인덱스는 25.0℃를 반환한다.</summary>
        public Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
        {
            EnsureConnected();
            return Task.FromResult(_bbCurrent.GetOrAdd(blackBodyIndex, 25.0f));
        }

        /// <summary>현재 온도만 기록하고 목표 온도는 저장하지 않는다.</summary>
        public Task WriteBlackBodyTemperaturesAsync(int blackBodyIndex, float currentTemperature, float targetTemperature)
        {
            EnsureConnected();
            _bbCurrent[blackBodyIndex] = currentTemperature;
            return Task.CompletedTask;
        }

        /// <summary>이동 시간 없이 즉시 도착 처리하고 현재 포인트를 갱신한다.</summary>
        public Task MoveServoToPositionAsync(int positionIndex)
        {
            EnsureConnected();
            lock (_gate) _currentPoint = positionIndex;
            Log($"MoveServoToPositionAsync({positionIndex}) -> arrived");
            return Task.CompletedTask;
        }

        /// <summary>마지막 이동 명령의 포인트 인덱스와 비교한 결과를 반환한다. 이동이 즉시 완료되므로 구동 중 상태는 없다.</summary>
        public Task<bool> IsServoAtPositionAsync(int positionIndex)
        {
            EnsureConnected();
            bool v;
            lock (_gate) v = _currentPoint == positionIndex;
            return Task.FromResult(v);
        }

        public Task SetServoSpeedAsync(int percent)
        {
            EnsureConnected();
            lock (_gate) _servoSpeedPercent = Math.Clamp(percent, 1, 100);
            Log($"SetServoSpeedAsync({percent}) -> {_servoSpeedPercent}%");
            return Task.CompletedTask;
        }

        /// <summary>로그만 남긴다. 서보 좌표는 변하지 않는다.</summary>
        public Task JogAsync(ServoAxis axis, bool positive, bool on)
        {
            EnsureConnected();
            Log($"JogAsync({axis}, positive={positive}, on={on})");
            return Task.CompletedTask;
        }

        /// <summary>해당 축 좌표를 즉시 0으로 되돌린다.</summary>
        public Task HomeAsync(ServoAxis axis)
        {
            EnsureConnected();
            lock (_gate)
            {
                if (axis == ServoAxis.X) _servoX = 0; else _servoY = 0;
            }
            Log($"HomeAsync({axis}) -> homed");
            return Task.CompletedTask;
        }

        public Task SetPointCoordinateAsync(int positionIndex, float x, float y)
        {
            EnsureConnected();
            _pointCoords[positionIndex] = (x, y);
            Log($"SetPointCoordinateAsync({positionIndex}, {x}, {y})");
            return Task.CompletedTask;
        }

        /// <summary>한 번도 쓰지 않은 포인트는 (0, 0)을 반환한다.</summary>
        public Task<(float X, float Y)> GetPointCoordinateAsync(int positionIndex)
        {
            EnsureConnected();
            return Task.FromResult(_pointCoords.GetOrAdd(positionIndex, (0f, 0f)));
        }

        /// <summary>이동 시간 없이 서보 좌표를 즉시 목표로 옮기고 현재 포인트를 0으로 둔다.</summary>
        public Task MoveToCoordinateAsync(float x, float y)
        {
            EnsureConnected();
            lock (_gate) { _servoX = x; _servoY = y; _currentPoint = 0; }
            Log($"MoveToCoordinateAsync({x}, {y}) -> arrived");
            return Task.CompletedTask;
        }

        public Task SetEquipmentAsync(PlcEquipment equipment, bool on)
        {
            EnsureConnected();
            _equipment[equipment] = on;
            Log($"SetEquipmentAsync({equipment}, {on})");
            return Task.CompletedTask;
        }

        public Task SetFanSpeedAsync(float hz)
        {
            EnsureConnected();
            lock (_gate) _fanHz = hz;
            Log($"SetFanSpeedAsync({hz})");
            return Task.CompletedTask;
        }

        public Task WriteAdminSettingsAsync(PlcAdminSettings settings)
        {
            EnsureConnected();
            _admin = settings;
            Log("WriteAdminSettingsAsync()");
            return Task.CompletedTask;
        }

        /// <summary>인메모리 상태를 스냅샷으로 만들어 반환한다. Heater는 항상 true이고 에러 플래그는 채우지 않는다.</summary>
        public Task<PlcStatusSnapshot> ReadStatusAsync()
        {
            EnsureConnected();
            var snap = new PlcStatusSnapshot();
            lock (_gate)
            {
                snap.CurrentTemperature = _currentTemp;
                snap.TargetTemperature = _targetTemp;
                snap.CurrentHumidity = _currentHum;
                snap.TargetHumidity = _targetHum;
                snap.CurrentPoint = _currentPoint;
                // 실 PLC 워드는 0.1mm 단위이므로 동일한 분해능으로 양자화.
                snap.ServoXPosition = MathF.Round(_servoX, 1);
                snap.ServoYPosition = MathF.Round(_servoY, 1);
                snap.FanSpeedHz = _fanHz;
                snap.Heater = true;
                snap.Blower1 = _equipment.GetValueOrDefault(PlcEquipment.Blower1);
                snap.Blower2 = _equipment.GetValueOrDefault(PlcEquipment.Blower2);
                snap.Cooler1st = _equipment.GetValueOrDefault(PlcEquipment.Cooler1st);
                snap.Cooler2nd = _equipment.GetValueOrDefault(PlcEquipment.Cooler2nd);
                snap.CoolerRoom = _equipment.GetValueOrDefault(PlcEquipment.CoolerRoom);
                snap.PairGlass = _equipment.GetValueOrDefault(PlcEquipment.PairGlass);
                snap.Admin = _admin;
            }
            snap.BlackBody1Pv = _bbCurrent.GetOrAdd(0, 25.0f);
            snap.BlackBody2Pv = _bbCurrent.GetOrAdd(1, 25.0f);
            return Task.FromResult(snap);
        }

        /// <summary>로그만 남긴다. 내부 상태 변화는 없다.</summary>
        public Task TriggerEmergencyStopAsync()
        {
            EnsureConnected();
            Log("TriggerEmergencyStopAsync() -> ESTOP");
            return Task.CompletedTask;
        }

        /// <summary>로그만 남긴다. 내부 상태 변화는 없다.</summary>
        public Task ResetErrorAsync()
        {
            EnsureConnected();
            Log("ResetErrorAsync() -> pulse");
            return Task.CompletedTask;
        }

        /// <summary>로그만 남긴다. 내부 상태 변화는 없다.</summary>
        public Task BuzzerOffAsync()
        {
            EnsureConnected();
            Log("BuzzerOffAsync() -> pulse");
            return Task.CompletedTask;
        }

        public void Dispose() => Disconnect();

        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("FakePlcController is not connected. Call ConnectAsync first.");
        }

        private static void Log(string msg)
            => Console.WriteLine($"[FakePlc] {msg}");
    }
}
