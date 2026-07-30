using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Master.Services;
using Moq;

namespace HeatingCameraSystem.Tests
{
    public class TemperatureRampControllerTests
    {
        [Fact]
        public async Task RampAsync_IncreasesMonotonicallyAndEndsAtExactTarget()
        {
            var values = new List<float>();
            var plc = new Mock<IPlcController>();
            plc.Setup(p => p.SetControlTemperatureAsync(It.IsAny<float>()))
                .Callback<float>(values.Add)
                .Returns(Task.CompletedTask);

            var now = DateTime.UtcNow;
            Task AdvanceTime(TimeSpan delay, CancellationToken _)
            {
                now += delay;
                return Task.CompletedTask;
            }

            var controller = new TemperatureRampController(plc.Object, 10, AdvanceTime, () => now);

            await controller.RampAsync(10f, 20f, 1, null, CancellationToken.None);

            Assert.NotEmpty(values);
            Assert.Equal(10f, values[0]);
            Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.First <= pair.Second));
            Assert.Equal(20f, values[^1]);
            plc.Verify(p => p.SetTargetTemperatureAsync(20f), Times.Once);
        }

        [Fact]
        public async Task RampAsync_DecreasesMonotonicallyFromCurrentToTarget()
        {
            var values = new List<float>();
            var plc = new Mock<IPlcController>();
            plc.Setup(p => p.SetControlTemperatureAsync(It.IsAny<float>()))
                .Callback<float>(values.Add)
                .Returns(Task.CompletedTask);
            var now = DateTime.UtcNow;
            Task AdvanceTime(TimeSpan delay, CancellationToken _)
            {
                now += delay;
                return Task.CompletedTask;
            }
            var controller = new TemperatureRampController(plc.Object, 10, AdvanceTime, () => now);

            await controller.RampAsync(30f, 20f, 1, null, CancellationToken.None);

            Assert.Equal(30f, values[0]);
            Assert.All(values.Zip(values.Skip(1)), pair => Assert.True(pair.First >= pair.Second));
            Assert.Equal(20f, values[^1]);
        }

        [Fact]
        public async Task RampAsync_CancellationStopsFurtherWrites()
        {
            var values = new List<float>();
            var plc = new Mock<IPlcController>();
            plc.Setup(p => p.SetControlTemperatureAsync(It.IsAny<float>()))
                .Callback<float>(values.Add)
                .Returns(Task.CompletedTask);

            using var cts = new CancellationTokenSource();
            var now = DateTime.UtcNow;
            Task CancelDuringDelay(TimeSpan delay, CancellationToken ct)
            {
                now += delay;
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            var controller = new TemperatureRampController(plc.Object, 10, CancelDuringDelay, () => now);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => controller.RampAsync(10f, 20f, 1, null, cts.Token));

            Assert.Single(values);
        }

        [Fact]
        public async Task RampAsync_NonPositiveMinutesWritesTargetOnce()
        {
            foreach (int minutes in new[] { 0, -1 })
            {
                var plc = new Mock<IPlcController>();
                var controller = new TemperatureRampController(plc.Object, 1);

                await controller.RampAsync(10f, 20f, minutes, null, CancellationToken.None);

                plc.Verify(p => p.SetTargetTemperatureAsync(20f), Times.Once);
                plc.Verify(p => p.SetControlTemperatureAsync(20f), Times.Once);
                plc.VerifyNoOtherCalls();
            }
        }
    }
}
