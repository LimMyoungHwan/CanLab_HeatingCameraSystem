using System.IO;
using System.Text.Json;
using HeatingCameraSystem.Core.Config;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class HardwareSettingsTests
    {
        [Fact]
        public void HardwareSettings_DefaultsArePopulated()
        {
            var s = new HardwareSettings();

            Assert.Equal("192.168.1.100", s.Plc.IpAddress);
            Assert.Equal(502, s.Plc.Port);
            Assert.Equal(100, s.Plc.RegTempPv);
            Assert.Equal(10, s.Plc.CoilRunStop);
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
                    Port = 502,
                    UnitId = 1,
                    RegTempPv = 4096,
                    RegTempSv = 4097,
                    CoilRunStop = 256
                },
                Nats = new NatsSettings { Url = "nats://master.local:4222" }
            };

            string json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<HardwareSettings>(json);

            Assert.NotNull(restored);
            Assert.Equal("10.0.1.50", restored!.Plc.IpAddress);
            Assert.Equal(1, restored.Plc.UnitId);
            Assert.Equal(4096, restored.Plc.RegTempPv);
            Assert.Equal(4097, restored.Plc.RegTempSv);
            Assert.Equal(256, restored.Plc.CoilRunStop);
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
            var loaded = JsonSerializer.Deserialize<HardwareSettings>(json);

            Assert.NotNull(loaded);
            Assert.Equal("10.0.1.50", loaded!.Plc.IpAddress);
            Assert.Equal(4096, loaded.Plc.RegTempPv);
            Assert.Equal(256, loaded.Plc.CoilRunStop);
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
