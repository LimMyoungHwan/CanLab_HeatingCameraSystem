using System;
using System.Net;
using System.Threading.Tasks;
using FluentModbus;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class PlcModbusClientTests : IDisposable
    {
        private ModbusTcpServer _server;
        private readonly int _testPort = 5020; // Use non-standard port for testing

        public PlcModbusClientTests()
        {
            _server = new ModbusTcpServer();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _testPort));
        }

        [Fact]
        public async Task ConnectAndDisconnect_ShouldSucceed()
        {
            using var client = new PlcModbusClient();
            await client.ConnectAsync("127.0.0.1", _testPort);
            
            client.Disconnect();
            Assert.True(true);
        }

        [Fact]
        public async Task SetTargetTemperature_ShouldWriteToHoldingRegister()
        {
            // Arrange
            using var client = new PlcModbusClient();
            await client.ConnectAsync("127.0.0.1", _testPort);
            float targetTemp = 35.5f;

            // Act
            await client.SetTargetTemperatureAsync(targetTemp);

            // Assert
            // RegTempSv is 101. 35.5f * 10 = 355
            short writtenValue = _server.GetHoldingRegisters()[101];
            Assert.Equal(355, writtenValue);
        }

        [Fact]
        public async Task StartChamber_ShouldWriteToCoil()
        {
            // Arrange
            using var client = new PlcModbusClient();
            await client.ConnectAsync("127.0.0.1", _testPort);

            // Act
            await client.StartChamberAsync();

            // Assert
            // CoilRunStop is 10.
            // Coil byte indexing: bit 10 is at byte 1 (10 / 8), bit 2 (10 % 8)
            byte coilByte = _server.GetCoils()[1];
            bool isSet = (coilByte & (1 << 2)) != 0;

            Assert.True(isSet);
        }
        
        [Fact]
        public async Task GetCurrentTemperature_ShouldReadHoldingRegister()
        {
            // Arrange
            // Set up server with specific temperature at register 100
            // Temperature 20.4 => 204
            _server.GetHoldingRegisters()[100] = 204;

            using var client = new PlcModbusClient();
            await client.ConnectAsync("127.0.0.1", _testPort);

            // Act
            float currentTemp = await client.GetCurrentTemperatureAsync();

            // Assert
            Assert.Equal(20.4f, currentTemp);
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }
    }
}

