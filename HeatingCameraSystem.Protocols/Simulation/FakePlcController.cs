using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;

namespace HeatingCameraSystem.Protocols.Simulation
{
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
        private int _servoX;
        private int _servoY;
        private int _servoSpeedPercent = 100;
        private float _fanHz;
        private readonly ConcurrentDictionary<int, float> _bbCurrent = new();
        private readonly ConcurrentDictionary<int, (int X, int Y)> _pointCoords = new();
        private readonly ConcurrentDictionary<PlcEquipment, bool> _equipment = new();
        private PlcAdminSettings _admin = new();

        public bool IsConnected
        {
            get { lock (_gate) return _isConnected; }
        }

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

        public Task StartChamberAsync()
        {
            EnsureConnected();
            Log("StartChamberAsync() -> RUN");
            return Task.CompletedTask;
        }

        public Task StopChamberAsync()
        {
            EnsureConnected();
            Log("StopChamberAsync() -> STOP");
            return Task.CompletedTask;
        }

        public Task SetTargetTemperatureAsync(float temperature)
        {
            EnsureConnected();
            lock (_gate) { _targetTemp = temperature; _currentTemp = temperature; }
            Log($"SetTargetTemperatureAsync({temperature}) -> current snaps to target");
            return Task.CompletedTask;
        }

        public Task<float> GetCurrentTemperatureAsync()
        {
            EnsureConnected();
            float v;
            lock (_gate) v = _currentTemp;
            return Task.FromResult(v);
        }

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

        public Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
        {
            EnsureConnected();
            _bbCurrent[blackBodyIndex] = temperature;
            Log($"SetBlackBodyTemperatureAsync(BB{blackBodyIndex}, {temperature}) -> snaps");
            return Task.CompletedTask;
        }

        public Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
        {
            EnsureConnected();
            return Task.FromResult(_bbCurrent.GetOrAdd(blackBodyIndex, 25.0f));
        }

        public Task MoveServoToPositionAsync(int positionIndex)
        {
            EnsureConnected();
            lock (_gate) _currentPoint = positionIndex;
            Log($"MoveServoToPositionAsync({positionIndex}) -> arrived");
            return Task.CompletedTask;
        }

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

        public Task JogAsync(ServoAxis axis, bool positive, bool on)
        {
            EnsureConnected();
            Log($"JogAsync({axis}, positive={positive}, on={on})");
            return Task.CompletedTask;
        }

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

        public Task SetPointCoordinateAsync(int positionIndex, int x, int y)
        {
            EnsureConnected();
            _pointCoords[positionIndex] = (x, y);
            Log($"SetPointCoordinateAsync({positionIndex}, {x}, {y})");
            return Task.CompletedTask;
        }

        public Task<(int X, int Y)> GetPointCoordinateAsync(int positionIndex)
        {
            EnsureConnected();
            return Task.FromResult(_pointCoords.GetOrAdd(positionIndex, (0, 0)));
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
                snap.ServoXPosition = _servoX;
                snap.ServoYPosition = _servoY;
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

        public Task TriggerEmergencyStopAsync()
        {
            EnsureConnected();
            Log("TriggerEmergencyStopAsync() -> ESTOP");
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
