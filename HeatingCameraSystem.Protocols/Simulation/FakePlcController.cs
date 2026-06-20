using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols.Simulation
{
    public class FakePlcController : IPlcController, IDisposable
    {
        private readonly object _gate = new();
        private bool _isConnected;
        private float _currentTemp;
        private float _currentHum;
        private bool _servoArrived = true;
        private readonly ConcurrentDictionary<int, float> _bbCurrent = new();

        public bool IsConnected
        {
            get { lock (_gate) return _isConnected; }
        }

        public Task ConnectAsync(string ipAddress, int port = 502)
        {
            lock (_gate)
            {
                _isConnected = true;
                _currentTemp = 25.0f;
                _currentHum  = 50.0f;
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
            lock (_gate) _currentTemp = temperature;
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
            lock (_gate) _currentHum = humidity;
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

        public Task MoveServoToPositionAsync(int positionIndex)
        {
            EnsureConnected();
            lock (_gate) _servoArrived = true;
            Log($"MoveServoToPositionAsync({positionIndex}) -> arrived");
            return Task.CompletedTask;
        }

        public Task<bool> IsServoAtPositionAsync()
        {
            EnsureConnected();
            bool v;
            lock (_gate) v = _servoArrived;
            return Task.FromResult(v);
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
