using System;
using System.IO;
using System.Linq;
using HeatingCameraSystem.AgentUI.Services;
using HeatingCameraSystem.AgentUI.ViewModels;
using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class SettingsViewModelLiveApplyTests
    {
        // agentui.json 경로가 LocalAppData로 고정이라, 개발 머신의 실제 설정을 건드리지 않도록
        // 테스트 동안 백업 후 복원한다. (테스트는 전역 비병렬이라 파일 경합 없음.)
        [Fact]
        public void Save_RemovingCamera_ShrinksConfigInPlaceAndRaisesSaved()
        {
            string path = Path.Combine(AgentUiConfig.ConfigDir, "agentui.json");
            byte[]? backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                var config = new AgentUiConfig
                {
                    SimulationMode = true,
                    Cameras =
                    {
                        new CameraDescriptor("Agent_0", 0, "Cam 0"),
                        new CameraDescriptor("Agent_1", 1, "Cam 1"),
                    }
                };

                var vm = new SettingsViewModel(config);
                object listBefore = config.Cameras;
                bool savedRaised = false;
                vm.Saved += () => savedRaised = true;

                CameraRow toRemove = vm.Cameras.First(row => row.AgentId == "Agent_1");
                vm.RemoveCameraCommand.Execute(toRemove);
                vm.SaveCommand.Execute(null);

                Assert.True(savedRaised);
                Assert.Same(listBefore, config.Cameras);
                Assert.Single(config.Cameras);
                Assert.Equal("Agent_0", config.Cameras[0].AgentId);
            }
            finally
            {
                if (backup is not null)
                {
                    File.WriteAllBytes(path, backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
