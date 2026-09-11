using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeatingCameraSystem.Core.Config;
using HeatingCameraSystem.Master.Services;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class HardwareSettingsTests
    {
        private static readonly JsonSerializerOptions Opts = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        [Fact]
        public void HardwareSettings_DefaultsArePopulated()
        {
            var s = new HardwareSettings();

            Assert.Equal("192.168.1.2", s.Plc.IpAddress);
            Assert.Equal(2004, s.Plc.Port);
            Assert.Equal("D100", s.Plc.TempPv);
            Assert.Equal("D102", s.Plc.TempSv);
            Assert.Equal(XgtCpuSeries.XGB, s.Plc.CpuSeries);
            Assert.True(s.Plc.UseHexBitIndex);
            Assert.Equal("M10", s.Plc.BitTempStart);
            Assert.Equal("M11", s.Plc.BitTempStopLamp);
            Assert.Equal("M21", s.Plc.BitTempStop);
            Assert.Equal("nats://127.0.0.1:4222", s.Nats.Url);
            Assert.Equal("COM3", s.Serial.PortName);
            Assert.Equal(0.5f, s.RecipeEngine.TemperatureTolerance);
            Assert.All(s.BlackBody.Units, unit => Assert.Equal(5200, unit.Port));
            Assert.Equal("M901", s.Plc.BitEmergencyStop);
            Assert.Equal("D281", s.Plc.BitHumidityControl);
        }

        [Fact]
        public void Simulate_DefaultsToEveryDevice_SoLegacyJsonKeepsBehaving()
        {
            var legacy = JsonSerializer.Deserialize<HardwareSettings>("""{"SimulationMode":true}""", Opts)!;

            Assert.True(AppServices.IsSimulated(legacy, s => s.Plc));
            Assert.True(AppServices.IsSimulated(legacy, s => s.BlackBody));
            Assert.True(AppServices.IsSimulated(legacy, s => s.Camera));
        }

        [Fact]
        public void IsSimulated_MasterSwitchOff_IgnoresPerDeviceSelection()
        {
            var settings = new HardwareSettings { SimulationMode = false };
            settings.Simulate.Plc = true;

            Assert.False(AppServices.IsSimulated(settings, s => s.Plc));
            Assert.Empty(AppServices.DescribeSimulated(settings));
        }

        [Fact]
        public void IsSimulated_PerDeviceSelection_LeavesOtherDevicesReal()
        {
            var settings = new HardwareSettings { SimulationMode = true };
            settings.Simulate.Camera = false;

            Assert.True(AppServices.IsSimulated(settings, s => s.Plc));
            Assert.True(AppServices.IsSimulated(settings, s => s.BlackBody));
            Assert.False(AppServices.IsSimulated(settings, s => s.Camera));
        }

        [Fact]
        public void HardwareSettings_RoundTripPreservesCustomValues()
        {
            var original = new HardwareSettings
            {
                Plc = new PlcSettings
                {
                    IpAddress = "10.0.1.50",
                    Port = 2004,
                    StationNo = 1,
                    CpuSeries = XgtCpuSeries.XGK,
                    UseHexBitIndex = false,
                    TempPv = "D200",
                    TempSv = "D202"
                },
                Nats = new NatsSettings { Url = "nats://master.local:4222" },
                BlackBody = new BlackBodySettings
                {
                    Enabled = true,
                    Units = new()
                    {
                        new BlackBodyUnitSettings
                        {
                            ConnectionType = BlackBodyConnectionType.Ip,
                            IpAddress = "10.0.1.80",
                            Port = 4001
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize(original, Opts);
            var restored = JsonSerializer.Deserialize<HardwareSettings>(json, Opts);

            Assert.NotNull(restored);
            Assert.Equal("10.0.1.50", restored!.Plc.IpAddress);
            Assert.Equal(1, restored.Plc.StationNo);
            Assert.Equal(XgtCpuSeries.XGK, restored.Plc.CpuSeries);
            Assert.False(restored.Plc.UseHexBitIndex);
            Assert.Equal("D200", restored.Plc.TempPv);
            Assert.Equal("D202", restored.Plc.TempSv);
            Assert.Equal("nats://master.local:4222", restored.Nats.Url);
            Assert.True(restored.BlackBody.Enabled);
            Assert.Equal(BlackBodyConnectionType.Ip, restored.BlackBody.Units[0].ConnectionType);
            Assert.Equal("10.0.1.80", restored.BlackBody.Units[0].IpAddress);
            Assert.Equal(4001, restored.BlackBody.Units[0].Port);
        }

        [Fact]
        public void CorrectLegacyHardwareSettings_ReplacesOnlyLegacyHardwareDefaults()
        {
            var settings = new HardwareSettings
            {
                Plc = new PlcSettings { BitEmergencyStop = "M2000", BitHumidityControl = "D281.0" },
                BlackBody = new BlackBodySettings
                {
                    Units = new() { new BlackBodyUnitSettings { ConnectionType = BlackBodyConnectionType.Ip, Port = 5000 } }
                }
            };

            bool changed = AppServices.CorrectLegacyHardwareSettings(settings);

            Assert.True(changed);
            Assert.Equal("M901", settings.Plc.BitEmergencyStop);
            Assert.Equal("D281", settings.Plc.BitHumidityControl);
            Assert.Equal(5200, settings.BlackBody.Units[0].Port);
        }

        [Fact]
        public void HardwareSettings_SampleFileMatchesSchema()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            string samplePath = Path.Combine(repoRoot, "docs", "samples", "hardware.json");

            Assert.True(File.Exists(samplePath), $"Sample missing at {samplePath}");

            var json = File.ReadAllText(samplePath);
            var loaded = JsonSerializer.Deserialize<HardwareSettings>(json, Opts);

            Assert.NotNull(loaded);
            Assert.Equal("10.0.1.50", loaded!.Plc.IpAddress);
            Assert.Equal(2004, loaded.Plc.Port);
            Assert.Equal("D100", loaded.Plc.TempPv);
            Assert.Equal("M11", loaded.Plc.BitTempStopLamp);
            Assert.Equal("M21", loaded.Plc.BitTempStop);
        }

        [Fact]
        public void AgentConfig_SampleFileMatchesSchema()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
            string samplePath = Path.Combine(repoRoot, "docs", "samples", "agent.json");

            Assert.True(File.Exists(samplePath), $"Sample missing at {samplePath}");

            var json = File.ReadAllText(samplePath);
            var loaded = JsonSerializer.Deserialize<AgentConfig>(json);

            Assert.NotNull(loaded);
            Assert.Equal("Agent_FloorA_Bay1", loaded!.AgentId);
            Assert.Equal("nats://master.local:4222", loaded.NatsUrl);
        }
    }
}
