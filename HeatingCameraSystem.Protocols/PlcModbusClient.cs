using System;
using System.Net;
using System.Threading.Tasks;
using FluentModbus;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    public class PlcModbusClient : IPlcController, IDisposable
    {
        private ModbusTcpClient _client;
        private bool _isConnected;
        private readonly PlcSettings _s;

        public bool IsConnected => _isConnected;

        public PlcModbusClient(PlcSettings? settings = null)
        {
            _s = settings ?? new PlcSettings();
            _client = new ModbusTcpClient();
        }

        public async Task ConnectAsync(string ipAddress, int port = 502)
        {
            if (_isConnected) return;
            await Task.Run(() =>
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                _client.Connect(endpoint);
                _isConnected = true;
            });
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                _client.Disconnect();
                _isConnected = false;
            }
        }

        public async Task StartChamberAsync()
        {
            CheckConnection();
            await Task.Run(() => _client.WriteSingleCoil((byte)_s.UnitId, _s.CoilRunStop, true));
        }

        public async Task StopChamberAsync()
        {
            CheckConnection();
            await Task.Run(() => _client.WriteSingleCoil((byte)_s.UnitId, _s.CoilRunStop, false));
        }

        public async Task SetTargetTemperatureAsync(float temperature)
        {
            CheckConnection();
            short val = (short)(temperature * 10);
            await Task.Run(() => _client.WriteSingleRegister((byte)_s.UnitId, _s.RegTempSv, val));
        }

        public async Task<float> GetCurrentTemperatureAsync()
        {
            CheckConnection();
            return await Task.Run(() =>
            {
                var regs = _client.ReadHoldingRegisters<short>((byte)_s.UnitId, _s.RegTempPv, 1);
                return regs.ToArray()[0] / 10f;
            });
        }

        public async Task SetTargetHumidityAsync(float humidity)
        {
            CheckConnection();
            short val = (short)(humidity * 10);
            await Task.Run(() => _client.WriteSingleRegister((byte)_s.UnitId, _s.RegHumSv, val));
        }

        public async Task<float> GetCurrentHumidityAsync()
        {
            CheckConnection();
            return await Task.Run(() =>
            {
                var regs = _client.ReadHoldingRegisters<short>((byte)_s.UnitId, _s.RegHumPv, 1);
                return regs.ToArray()[0] / 10f;
            });
        }

        public async Task MoveServoToPositionAsync(int positionIndex)
        {
            CheckConnection();
            await Task.Run(() =>
            {
                _client.WriteSingleCoil((byte)_s.UnitId, _s.CoilServoArrival, false);
                _client.WriteSingleRegister((byte)_s.UnitId, _s.RegServoPosSv, (short)positionIndex);
            });
        }

        public async Task<bool> IsServoAtPositionAsync()
        {
            CheckConnection();
            return await Task.Run(() =>
            {
                var coils = _client.ReadCoils((byte)_s.UnitId, _s.CoilServoArrival, 1);
                return coils.ToArray()[0] > 0;
            });
        }

        public async Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
        {
            CheckConnection();
            short val = (short)(temperature * 10);
            int reg = _s.RegBb1TempSv + blackBodyIndex;
            await Task.Run(() => _client.WriteSingleRegister((byte)_s.UnitId, reg, val));
        }

        public async Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
        {
            CheckConnection();
            int reg = _s.RegBb1TempPv + blackBodyIndex;
            return await Task.Run(() =>
            {
                var regs = _client.ReadHoldingRegisters<short>((byte)_s.UnitId, reg, 1);
                return regs.ToArray()[0] / 10f;
            });
        }

        public async Task TriggerEmergencyStopAsync()
        {
            CheckConnection();
            await Task.Run(() => _client.WriteSingleCoil((byte)_s.UnitId, _s.CoilEStop, true));
        }

        private void CheckConnection()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to PLC.");
        }

        public void Dispose()
        {
            Disconnect();
            _client?.Dispose();
        }
    }
}
