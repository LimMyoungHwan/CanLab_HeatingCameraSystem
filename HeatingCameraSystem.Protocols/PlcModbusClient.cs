using System;
using System.Net;
using System.Threading.Tasks;
using FluentModbus;
using HeatingCameraSystem.Core.Interfaces;

namespace HeatingCameraSystem.Protocols
{
    public class PlcModbusClient : IPlcController, IDisposable
    {
        private ModbusTcpClient _client;
        private bool _isConnected;
        
        // Placeholder Registers (Holding)
        private const int RegTempPv = 100;
        private const int RegTempSv = 101;
        private const int RegHumPv = 102;
        private const int RegHumSv = 103;
        private const int RegServoPosSv = 104;

        // Placeholder Coils
        private const int CoilRunStop = 10;
        private const int CoilServoArrival = 11;
        private const int CoilEStop = 12;

        public PlcModbusClient()
        {
            _client = new ModbusTcpClient();
        }

        public async Task ConnectAsync(string ipAddress, int port = 502)
        {
            if (_isConnected) return;
            
            // Note: FluentModbus Connect method is synchronous but we wrap it logically
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
            await Task.Run(() => _client.WriteSingleCoil(0, CoilRunStop, true));
        }

        public async Task StopChamberAsync()
        {
            CheckConnection();
            await Task.Run(() => _client.WriteSingleCoil(0, CoilRunStop, false));
        }

        public async Task SetTargetTemperatureAsync(float temperature)
        {
            CheckConnection();
            // Assuming temperature is scaled by 10 (e.g., 25.5 -> 255)
            short val = (short)(temperature * 10);
            await Task.Run(() => _client.WriteSingleRegister(0, RegTempSv, val));
        }

        public async Task<float> GetCurrentTemperatureAsync()
        {
            CheckConnection();
            return await Task.Run(() => 
            {
                var registers = _client.ReadHoldingRegisters<short>(0, RegTempPv, 1);
                return registers.ToArray()[0] / 10f;
            });
        }

        public async Task SetTargetHumidityAsync(float humidity)
        {
            CheckConnection();
            short val = (short)(humidity * 10);
            await Task.Run(() => _client.WriteSingleRegister(0, RegHumSv, val));
        }

        public async Task<float> GetCurrentHumidityAsync()
        {
            CheckConnection();
            return await Task.Run(() => 
            {
                var registers = _client.ReadHoldingRegisters<short>(0, RegHumPv, 1);
                return registers.ToArray()[0] / 10f;
            });
        }

        public async Task MoveServoToPositionAsync(int positionIndex)
        {
            CheckConnection();
            // Reset arrival coil first, then write position
            await Task.Run(() => 
            {
                _client.WriteSingleCoil(0, CoilServoArrival, false);
                _client.WriteSingleRegister(0, RegServoPosSv, (short)positionIndex);
            });
        }

        public async Task<bool> IsServoAtPositionAsync()
        {
            CheckConnection();
            return await Task.Run(() => 
            {
                var coils = _client.ReadCoils(0, CoilServoArrival, 1);
                return coils.ToArray()[0] > 0;
            });
        }

        public async Task SetBlackBodyTemperatureAsync(int blackBodyIndex, float temperature)
        {
            CheckConnection();
            short val = (short)(temperature * 10);
            // Assuming RegBb1TempSv = 105, RegBb2TempSv = 106
            int regAddress = 105 + blackBodyIndex;
            await Task.Run(() => _client.WriteSingleRegister(0, regAddress, val));
        }

        public async Task<float> GetCurrentBlackBodyTemperatureAsync(int blackBodyIndex)
        {
            CheckConnection();
            // Assuming RegBb1TempPv = 107, RegBb2TempPv = 108
            int regAddress = 107 + blackBodyIndex;
            return await Task.Run(() => 
            {
                var registers = _client.ReadHoldingRegisters<short>(0, regAddress, 1);
                return registers.ToArray()[0] / 10f;
            });
        }

        public async Task TriggerEmergencyStopAsync()
        {
            CheckConnection();
            await Task.Run(() => _client.WriteSingleCoil(0, CoilEStop, true));
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
