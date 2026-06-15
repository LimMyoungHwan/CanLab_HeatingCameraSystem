using System;
using System.Net;
using System.Threading.Tasks;
using FluentModbus;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class PlcModbusClientTests : IDisposable
    {
        private readonly PlcSettings _s = new PlcSettings();
        private ModbusTcpServer _server;
        private readonly int _testPort = 5020;

        public PlcModbusClientTests()
        {
            _server = new ModbusTcpServer();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _testPort));
        }

        [Fact]
        public async Task ConnectAndDisconnect_ShouldSucceed()
        {
            using var client = new PlcModbusClient(_s);
            await client.ConnectAsync("127.0.0.1", _testPort);
            client.Disconnect();
            Assert.True(true);
        }

        [Fact]
        public async Task SetTargetTemperature_ShouldWriteToHoldingRegister()
        {
            using var client = new PlcModbusClient(_s);
            await client.ConnectAsync("127.0.0.1", _testPort);
            float targetTemp = 35.5f;

            await client.SetTargetTemperatureAsync(targetTemp);

            short writtenValue = _server.GetHoldingRegisters()[_s.RegTempSv];
            Assert.Equal((short)(targetTemp * 10), writtenValue);
        }

        [Fact]
        public async Task StartChamber_ShouldWriteToCoil()
        {
            using var client = new PlcModbusClient(_s);
            await client.ConnectAsync("127.0.0.1", _testPort);

            await client.StartChamberAsync();

            byte coilByte = _server.GetCoils()[_s.CoilRunStop / 8];
            bool isSet = (coilByte & (1 << (_s.CoilRunStop % 8))) != 0;
            Assert.True(isSet);
        }

        [Fact]
        public async Task GetCurrentTemperature_ShouldReadHoldingRegister()
        {
            short encodedTemp = 204;
            _server.GetHoldingRegisters()[_s.RegTempPv] = encodedTemp;

            using var client = new PlcModbusClient(_s);
            await client.ConnectAsync("127.0.0.1", _testPort);

            float currentTemp = await client.GetCurrentTemperatureAsync();

            Assert.Equal(encodedTemp / 10f, currentTemp);
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }
    }
}

