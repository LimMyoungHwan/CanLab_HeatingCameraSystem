using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HeatingCameraSystem.Master.Services;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.Tests
{
    [CollectionDefinition("Shutdown", DisableParallelization = true)]
    public class ShutdownCollection { }

    [Collection("Shutdown")]
    public class AppServicesShutdownTests
    {
        private static (string, Func<Task>) Record(string name, List<string> log)
            => (name, () => { log.Add(name); return Task.CompletedTask; });

        [Fact]
        public async Task RunShutdownAsync_StopsChamberBeforeDisposingResources_InOrder()
        {
            var log = new List<string>();

            await AppServices.RunShutdownAsync(
                () => { log.Add("stopChamber"); return Task.CompletedTask; },
                new[] { Record("a", log), Record("b", log), Record("c", log) },
                TimeSpan.FromSeconds(1));

            Assert.Equal(new[] { "stopChamber", "a", "b", "c" }, log);
        }

        [Fact]
        public async Task RunShutdownAsync_NoController_SkipsChamberStop_StillDisposes()
        {
            var log = new List<string>();

            await AppServices.RunShutdownAsync(
                null,
                new[] { Record("only", log) },
                TimeSpan.FromSeconds(1));

            Assert.Equal(new[] { "only" }, log);
        }

        [Fact]
        public async Task RunShutdownAsync_StepThrows_LaterStepsStillRun()
        {
            var log = new List<string>();

            await AppServices.RunShutdownAsync(
                null,
                new (string, Func<Task>)[]
                {
                    ("boom", () => throw new InvalidOperationException("dispose x")),
                    Record("after", log),
                },
                TimeSpan.FromSeconds(1));

            Assert.Contains("after", log);
        }

        [Fact]
        public async Task RunShutdownAsync_ChamberStopThrows_RaisesAlarm_AndContinuesCleanup()
        {
            AlarmSink.Entries.Clear();
            var log = new List<string>();

            await AppServices.RunShutdownAsync(
                () => throw new InvalidOperationException("chamber boom"),
                new[] { Record("dispose", log) },
                TimeSpan.FromSeconds(1));

            Assert.Contains("dispose", log);
            Assert.Contains(AlarmSink.Entries,
                e => e.Severity == AlarmSeverity.Error && e.Source == "PLC" && e.Message.Contains("챔버 정지 실패"));
        }

        [Fact]
        public async Task RunShutdownAsync_ChamberStopHangs_BoundedByTimeout_AndContinuesCleanup()
        {
            var log = new List<string>();
            var sw = Stopwatch.StartNew();

            await AppServices.RunShutdownAsync(
                () => new TaskCompletionSource<bool>().Task,
                new[] { Record("dispose", log) },
                TimeSpan.FromMilliseconds(200));
            sw.Stop();

            Assert.Contains("dispose", log);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"chamber stop not bounded; elapsed={sw.Elapsed}");
        }

        [Fact]
        public async Task RunShutdownAsync_SimulatorPlc_StopsChamberCleanly_ThenDisposes()
        {
            AlarmSink.Entries.Clear();
            var plc = new FakePlcController();
            await plc.ConnectAsync("127.0.0.1");
            var log = new List<string>();

            await AppServices.RunShutdownAsync(
                plc.StopChamberAsync,
                new (string, Func<Task>)[]
                {
                    ("plc", () => { plc.Dispose(); log.Add("plc"); return Task.CompletedTask; }),
                },
                TimeSpan.FromSeconds(2));

            Assert.Contains("plc", log);
            Assert.DoesNotContain(AlarmSink.Entries, e => e.Message.Contains("챔버 정지 실패"));
        }
    }
}
