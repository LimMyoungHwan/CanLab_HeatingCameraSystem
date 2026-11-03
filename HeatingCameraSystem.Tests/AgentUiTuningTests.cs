using HeatingCameraSystem.AgentUI.Services;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class AgentUiTuningTests
    {
        [Fact]
        public void OutOfRangeValuesFallBackToDefaults()
        {
            var tuning = new AgentUiTuning
            {
                SerialPollIntervalMs = 0,
                CameraStartStaggerMs = -1,
                HotplugDebounceMs = 10,
                SerialOpenTimeoutMs = 0,
                PairingTimeoutMs = 0,
            }.Normalized();

            Assert.Equal(1000, tuning.SerialPollIntervalMs);
            Assert.Equal(0, tuning.CameraStartStaggerMs);
            Assert.Equal(1000, tuning.HotplugDebounceMs);
            Assert.Equal(5000, tuning.SerialOpenTimeoutMs);
            Assert.Equal(30_000, tuning.PairingTimeoutMs);
        }

        [Fact]
        public void MissingTuningUsesDefaults()
        {
            Assert.Equal(1000, new AgentUiConfig().EffectiveTuning.SerialPollIntervalMs);
        }
    }
}
