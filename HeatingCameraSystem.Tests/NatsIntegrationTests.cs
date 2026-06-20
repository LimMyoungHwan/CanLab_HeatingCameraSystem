using System;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests
{
    [Trait("Category", "Integration")]
    public class NatsIntegrationTests
    {
        private const string NatsUrl = "nats://127.0.0.1:4222";

        [Fact]
        public async Task PublishSubscribe_CaptureCommand_RoundTrip()
        {
            if (!await IsNatsAvailableAsync(NatsUrl))
            {
                Console.WriteLine($"[SKIP] NATS not reachable at {NatsUrl}");
                return;
            }

            await using var pub = new NatsCommunicationService();
            await pub.ConnectAsync(NatsUrl);

            await using var sub = new NatsCommunicationService();
            await sub.ConnectAsync(NatsUrl);

            var received = new TaskCompletionSource<CaptureCommandMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await sub.SubscribeCaptureCommandAsync("Agent_99", cmd => received.TrySetResult(cmd));

            await Task.Delay(300);

            string stepId = $"itest-{Guid.NewGuid():N}";
            await pub.PublishCaptureCommandAsync(new CaptureCommandMessage
            {
                TargetAgentId = "Agent_99",
                RecipeStepId  = stepId,
                Timestamp     = DateTime.UtcNow
            });

            var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(received.Task, done);
            var result = await received.Task;
            Assert.Equal(stepId, result.RecipeStepId);
        }

        [Fact]
        public async Task PublishSubscribe_CaptureResult_RoundTrip()
        {
            if (!await IsNatsAvailableAsync(NatsUrl))
            {
                Console.WriteLine($"[SKIP] NATS not reachable at {NatsUrl}");
                return;
            }

            await using var pub = new NatsCommunicationService();
            await pub.ConnectAsync(NatsUrl);

            await using var sub = new NatsCommunicationService();
            await sub.ConnectAsync(NatsUrl);

            var received = new TaskCompletionSource<CaptureResultMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await sub.SubscribeCaptureResultAsync(r => received.TrySetResult(r));

            await Task.Delay(300);

            string stepId = $"itest-{Guid.NewGuid():N}";
            await pub.PublishCaptureResultAsync(new CaptureResultMessage
            {
                AgentId      = "Agent_99",
                RecipeStepId = stepId,
                IsSuccess    = true,
                ImagePath    = "/tmp/x.jpg",
                Timestamp    = DateTime.UtcNow
            });

            var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(received.Task, done);
            var result = await received.Task;
            Assert.Equal(stepId, result.RecipeStepId);
            Assert.True(result.IsSuccess);
        }

        private static async Task<bool> IsNatsAvailableAsync(string url)
        {
            try
            {
                await using var probe = new NatsCommunicationService();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var connectTask = probe.ConnectAsync(url);
                var done = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, cts.Token));
                return done == connectTask && !connectTask.IsFaulted;
            }
            catch
            {
                return false;
            }
        }
    }
}
