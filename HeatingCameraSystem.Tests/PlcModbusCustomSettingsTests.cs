using System;
using System.Net;
using System.Threading.Tasks;
using FluentModbus;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class PlcModbusCustomSettingsTests : IDisposable
    {
        private readonly ModbusTcpServer _server;
        private readonly int _port = 5025;

        private readonly PlcSettings _custom = new()
        {
            UnitId = 0,
            RegTempPv = 4096,
            RegTempSv = 4097,
            RegHumPv = 4098,
            RegHumSv = 4099,
            RegServoPosSv = 4100,
            RegBb1TempSv = 4101,
            RegBb2TempSv = 4102,
            RegBb1TempPv = 4103,
            RegBb2TempPv = 4104,
            CoilRunStop = 256,
            CoilServoArrival = 257,
            CoilEStop = 258
        };

        public PlcModbusCustomSettingsTests()
        {
            _server = new ModbusTcpServer();
            _server.Start(new IPEndPoint(IPAddress.Loopback, _port));
        }

        [Fact]
        public async Task SetTargetTemperature_UsesCustomRegisterAddress()
        {
            using var client = new PlcModbusClient(_custom);
            await client.ConnectAsync("127.0.0.1", _port);

            await client.SetTargetTemperatureAsync(42.5f);

            Assert.Equal(425, _server.GetHoldingRegisters()[_custom.RegTempSv]);
            Assert.Equal(0, _server.GetHoldingRegisters()[101]);
        }

        [Fact]
        public async Task StartChamber_UsesCustomCoilAddress()
        {
            using var client = new PlcModbusClient(_custom);
            await client.ConnectAsync("127.0.0.1", _port);

            await client.StartChamberAsync();

            byte coilByte = _server.GetCoils()[_custom.CoilRunStop / 8];
            bool isSet = (coilByte & (1 << (_custom.CoilRunStop % 8))) != 0;
            Assert.True(isSet);

            Assert.Equal(0, _server.GetCoils()[1]);
        }

        [Fact]
        public async Task GetBlackBodyTemperature_UsesCustomBaseAddress()
        {
            _server.GetHoldingRegisters()[_custom.RegBb1TempPv] = 600;
            _server.GetHoldingRegisters()[_custom.RegBb1TempPv + 1] = 800;

            using var client = new PlcModbusClient(_custom);
            await client.ConnectAsync("127.0.0.1", _port);

            float bb0 = await client.GetCurrentBlackBodyTemperatureAsync(0);
            float bb1 = await client.GetCurrentBlackBodyTemperatureAsync(1);

            Assert.Equal(60.0f, bb0);
            Assert.Equal(80.0f, bb1);
        }

        [Fact]
        public async Task ServoMovement_UsesCustomRegistersAndCoils()
        {
            using var client = new PlcModbusClient(_custom);
            await client.ConnectAsync("127.0.0.1", _port);

            await client.MoveServoToPositionAsync(12);

            Assert.Equal(12, _server.GetHoldingRegisters()[_custom.RegServoPosSv]);
        }

        public void Dispose()
        {
            _server.Stop();
            _server.Dispose();
        }
    }
}
