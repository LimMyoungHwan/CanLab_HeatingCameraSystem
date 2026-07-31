using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.AgentUI.Services;

namespace HeatingCameraSystem.Tests.AgentUI
{
    public class AppShutdownTests
    {
        [Fact]
        public void Run_ReturnsFalseWithinTimeout_WhenStepHangs()
        {
            var sw = Stopwatch.StartNew();
            bool result = AppShutdown.Run(new Func<Task>[]
            {
                () => new TaskCompletionSource<bool>().Task // never completes
            }, TimeSpan.FromMilliseconds(500));
            sw.Stop();

            Assert.False(result);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"did not return promptly; elapsed={sw.Elapsed}");
        }

        [Fact]
        public void Run_ReturnsTrue_WhenAllStepsComplete()
        {
            bool result = AppShutdown.Run(new Func<Task>[]
            {
                () => Task.CompletedTask,
                () => Task.Delay(10)
            }, TimeSpan.FromSeconds(5));

            Assert.True(result);
        }

        [Fact]
        public void Run_ContinuesAfterAStepThrows()
        {
            int counter = 0;
            AppShutdown.Run(new Func<Task>[]
            {
                () => throw new InvalidOperationException("boom"),
                () => { counter++; return Task.CompletedTask; }
            }, TimeSpan.FromSeconds(5));

            Assert.Equal(1, counter);
        }

        [Fact]
        public void Run_RunsStepsOffCallingThread()
        {
            int callingThreadId = Environment.CurrentManagedThreadId;
            int stepThreadId = -1;

            AppShutdown.Run(new Func<Task>[]
            {
                () => { stepThreadId = Environment.CurrentManagedThreadId; return Task.CompletedTask; }
            }, TimeSpan.FromSeconds(5));

            Assert.NotEqual(callingThreadId, stepThreadId);
        }
    }
}
