using System.Linq;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class SettingsViewModelDuplicateTests
    {
        [Fact]
        public void Save_DuplicateAgentId_RefusesAndKeepsConfig()
        {
            var config = new AgentUiConfig
            {
                SimulationMode = true,
                Cameras =
                {
                    new CameraDescriptor("Agent_0", 0, "Cam 0", "COM4"),
                    new CameraDescriptor("Agent_0", 1, "Cam 1", "COM5"),
                }
            };

            var vm = new SettingsViewModel(config);
            bool savedRaised = false;
            vm.Saved += () => savedRaised = true;

            vm.SaveCommand.Execute(null);

            Assert.False(savedRaised);
            Assert.Equal(2, config.Cameras.Count);
            Assert.Contains("중복", vm.StatusText);
        }

        [Fact]
        public void Save_DuplicatePort_RefusesAndKeepsConfig()
        {
            var config = new AgentUiConfig
            {
                SimulationMode = true,
                Cameras =
                {
                    new CameraDescriptor("Agent_0", 0, "Cam 0", "COM4"),
                    new CameraDescriptor("Agent_1", 1, "Cam 1", "COM4"),
                }
            };

            var vm = new SettingsViewModel(config);
            bool savedRaised = false;
            vm.Saved += () => savedRaised = true;

            vm.SaveCommand.Execute(null);

            Assert.False(savedRaised);
            Assert.Equal(2, config.Cameras.Count);
            Assert.Contains("중복", vm.StatusText);
        }

        [Fact]
        public void AddCamera_GeneratesUniqueIds()
        {
            var config = new AgentUiConfig { SimulationMode = true };
            var vm = new SettingsViewModel(config);

            vm.AddCameraCommand.Execute(null);
            vm.AddCameraCommand.Execute(null);

            Assert.Equal(2, vm.Cameras.Count);
            Assert.NotEqual(vm.Cameras[0].AgentId, vm.Cameras[1].AgentId);
            Assert.NotEqual(vm.Cameras[0].OpenCvIndex, vm.Cameras[1].OpenCvIndex);
        }
    }
}
