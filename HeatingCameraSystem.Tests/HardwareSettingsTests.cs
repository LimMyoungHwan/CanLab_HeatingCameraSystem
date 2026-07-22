using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeatingCameraSystem.Core.Config;
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
            Assert.Equal("nats://127.0.0.1:4222", s.Nats.Url);
            Assert.Equal("COM3", s.Serial.PortName);
            Assert.Equal(0.5f, s.RecipeEngine.TemperatureTolerance);
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
                Nats = new NatsSettings { Url = "nats://master.local:4222" }
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
