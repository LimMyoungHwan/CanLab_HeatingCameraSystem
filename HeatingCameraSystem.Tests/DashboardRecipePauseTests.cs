using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Master.ViewModels;
using Moq;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    public class DashboardRecipePauseTests
    {
        private static DashboardViewModel CreateVm()
        {
            var nats = new Mock<INatsCommunicationService>();
            nats.Setup(n => n.SubscribeAgentStatusAsync(It.IsAny<Action<AgentStatusMessage>>())).Returns(Task.CompletedTask);
            nats.Setup(n => n.SubscribeLiveFrameAsync(It.IsAny<Action<LiveFrameMessage>>())).Returns(Task.CompletedTask);
            return new DashboardViewModel(null, nats.Object, null, startTimers: false);
        }

        private static Task InvokeWaitForResume(DashboardViewModel vm, CancellationToken ct)
        {
            var m = typeof(DashboardViewModel).GetMethod("WaitForResumeAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (Task)m.Invoke(vm, new object[] { ct })!;
        }

        [Fact]
        public void Initial_NotPaused_ResumeDisabled()
        {
            var vm = CreateVm();
            Assert.False(vm.IsRecipePaused);
            Assert.False(vm.ResumeRecipeCommand.CanExecute(null));
        }

        [Fact]
        public async Task WaitForResume_PausesThenResumeProceeds()
        {
            var vm = CreateVm();
            var task = InvokeWaitForResume(vm, CancellationToken.None);

            Assert.True(vm.IsRecipePaused);
            Assert.True(vm.ResumeRecipeCommand.CanExecute(null));

            vm.ResumeRecipeCommand.Execute(null);
            await task;

            Assert.False(vm.IsRecipePaused);
        }

        [Fact]
        public async Task WaitForResume_Cancelled_ThrowsAndClearsPaused()
        {
            var vm = CreateVm();
            using var cts = new CancellationTokenSource();
            var task = InvokeWaitForResume(vm, cts.Token);

            Assert.True(vm.IsRecipePaused);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            Assert.False(vm.IsRecipePaused);
        }
    }
}
