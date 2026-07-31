using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeatingCameraSystem.Protocols;
using Xunit;

namespace HeatingCameraSystem.Tests.Protocols
{
    // Targets the internal self-healing seam NatsCommunicationService.RunSubscribeWithRetryAsync<T>.
    // Uses plain int payloads and hand-written IAsyncEnumerable factories - NO live NATS server.
    public class NatsSubscribeRetryTests
    {
        [Fact]
        public async Task ReSubscribesAfterFactoryThrows_AndInvokesCallback()
        {
            using var cts = new CancellationTokenSource();
            int factoryCalls = 0;
            int? received = null;

            async IAsyncEnumerable<int> Attempt()
            {
                int n = Interlocked.Increment(ref factoryCalls);
                await Task.Yield();
                if (n == 1)
                    throw new InvalidOperationException("transient disconnect");
                yield return 42;
            }

            await NatsCommunicationService.RunSubscribeWithRetryAsync<int>(
                _ => Attempt(),
                v => { received = v; cts.Cancel(); },
                _ => TimeSpan.FromMilliseconds(5),
                cts.Token);

            Assert.Equal(42, received);
            Assert.True(factoryCalls >= 2, $"expected >= 2 factory calls, got {factoryCalls}");
        }

        [Fact]
        public async Task ReSubscribesWhenEnumeratorCompletesNaturally()
        {
            using var cts = new CancellationTokenSource();
            int factoryCalls = 0;
            int? received = null;

            async IAsyncEnumerable<int> Attempt()
            {
                int n = Interlocked.Increment(ref factoryCalls);
                await Task.Yield();
                if (n == 1)
                    yield break; // zero items, silent natural completion (connection drop)
                yield return 7;
            }

            await NatsCommunicationService.RunSubscribeWithRetryAsync<int>(
                _ => Attempt(),
                v => { received = v; cts.Cancel(); },
                _ => TimeSpan.FromMilliseconds(5),
                cts.Token);

            Assert.Equal(7, received);
            Assert.True(factoryCalls >= 2, $"expected >= 2 factory calls, got {factoryCalls}");
        }

        [Fact]
        public async Task StopsPromptlyOnCancellation()
        {
            using var cts = new CancellationTokenSource();
            int factoryCalls = 0;

            async IAsyncEnumerable<int> AlwaysThrow()
            {
                int n = Interlocked.Increment(ref factoryCalls);
                await Task.Yield();
                if (n > 0) // always true at runtime, not a compile-time constant
                    throw new InvalidOperationException("always down");
                yield return 0;
            }

            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            var sw = Stopwatch.StartNew();

            await NatsCommunicationService.RunSubscribeWithRetryAsync<int>(
                _ => AlwaysThrow(),
                _ => { },
                _ => TimeSpan.FromMilliseconds(20),
                cts.Token);

            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 500, $"expected prompt stop, took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task IsolatesCallbackExceptions()
        {
            using var cts = new CancellationTokenSource();
            int? second = null;
            bool firstSeen = false;

            async IAsyncEnumerable<int> TwoItems()
            {
                await Task.Yield();
                yield return 1;
                yield return 2;
            }

            await NatsCommunicationService.RunSubscribeWithRetryAsync<int>(
                _ => TwoItems(),
                v =>
                {
                    if (!firstSeen)
                    {
                        firstSeen = true;
                        throw new InvalidOperationException("callback boom");
                    }
                    second = v;
                    cts.Cancel();
                },
                _ => TimeSpan.FromMilliseconds(5),
                cts.Token);

            Assert.Equal(2, second);
        }

        [Fact]
        public async Task HonorsBackoffBetweenAttempts()
        {
            using var cts = new CancellationTokenSource();
            int factoryCalls = 0;

            async IAsyncEnumerable<int> AlwaysThrow()
            {
                int n = Interlocked.Increment(ref factoryCalls);
                await Task.Yield();
                if (n >= 3)
                    cts.Cancel();
                if (n > 0) // always true at runtime, not a compile-time constant
                    throw new InvalidOperationException("down");
                yield return 0;
            }

            var sw = Stopwatch.StartNew();

            await NatsCommunicationService.RunSubscribeWithRetryAsync<int>(
                _ => AlwaysThrow(),
                _ => { },
                _ => TimeSpan.FromMilliseconds(50),
                cts.Token);

            sw.Stop();
            Assert.True(factoryCalls >= 3, $"expected >= 3 attempts, got {factoryCalls}");
            Assert.True(sw.ElapsedMilliseconds >= 90, $"expected backoff >= ~100ms, got {sw.ElapsedMilliseconds}ms");
        }
    }
}
