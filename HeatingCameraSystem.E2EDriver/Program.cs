using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Interfaces;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.E2EDriver;

internal static class Program
{
    private const int ExitPass = 0;
    private const int ExitVerificationFailed = 1;
    private const int ExitNatsConnectFailed = 2;
    private const int ExitCameraTimeout = 3;
    private const int ExitPlcConnectFailed = 4;
    private const int ExitPlcOperationFailed = 5;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--external-simulator", StringComparison.OrdinalIgnoreCase))
            return await RunExternalSimulatorAsync(args);

        string natsUrl = args.Length > 0 ? args[0] : "nats://127.0.0.1:4222";
        int timeoutSec = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 30;

        var plc = new FakePlcController();
        await plc.ConnectAsync("any");
        var blackBody = new PlcBlackBodyAdapter(plc);
        return await RunRecipeAsync(plc, blackBody, natsUrl, timeoutSec, externalMode: false);
    }

    private static async Task<int> RunExternalSimulatorAsync(string[] args)
    {
        string natsUrl = args.Length > 1 ? args[1] : "nats://127.0.0.1:4222";
        string plcHost = args.Length > 2 ? args[2] : "127.0.0.1";
        int plcPort = args.Length > 3 && int.TryParse(args[3], out int p) ? p : 2004;
        int timeoutSec = args.Length > 4 && int.TryParse(args[4], out int t) ? t : 30;

        var plc = new PlcXgtClient();
        try
        {
            await plc.ConnectAsync(plcHost, plcPort);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[E2E] FAIL - PLC connect {plcHost}:{plcPort}: {ex.Message}");
            return ExitPlcConnectFailed;
        }

        using (plc)
        using (var blackBody = new PlcBlackBodyAdapter(plc))
        {
            return await RunRecipeAsync(plc, blackBody, natsUrl, timeoutSec, externalMode: true);
        }
    }

    private static async Task<int> RunRecipeAsync(
        IPlcController plc,
        IBlackBodyController blackBody,
        string natsUrl,
        int timeoutSec,
        bool externalMode)
    {
        Console.WriteLine($"[E2E] Mode = {(externalMode ? "external simulator" : "internal fake")}");
        Console.WriteLine($"[E2E] NATS = {natsUrl}, capture timeout = {timeoutSec}s");

        await using var nats = new NatsCommunicationService();
        try
        {
            await nats.ConnectAsync(natsUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[E2E] FAIL - NATS connect: {ex.Message}");
            return ExitNatsConnectFailed;
        }
        Console.WriteLine("[E2E] NATS connected.");

        var captures = new ConcurrentBag<CaptureResultMessage>();
        var waiters  = new ConcurrentDictionary<string, TaskCompletionSource<CaptureResultMessage>>();
        await nats.SubscribeCaptureResultAsync(r =>
        {
            captures.Add(r);
            if (waiters.TryGetValue(r.RecipeStepId, out var tcs)) tcs.TrySetResult(r);
            Console.WriteLine($"[E2E]   <- capture result: agent={r.AgentId}, success={r.IsSuccess}, path={r.ImagePath}");
        });

        var recipe = new Recipe
        {
            Name                    = "E2E_SimRecipe",
            GlobalTargetTemperature = 30.0f,
            GlobalTargetHumidity    = 55.0f,
            Steps = new List<RecipeStep>
            {
                new() { CameraIndex = 0, TargetPositionIndex = 1, TargetBlackBodyTemperature = 35.0f },
                new() { CameraIndex = 1, TargetPositionIndex = 2, TargetBlackBodyTemperature = 40.0f },
                new() { CameraIndex = 0, TargetPositionIndex = 3, TargetBlackBodyTemperature = 45.0f },
                new() { CameraIndex = 1, TargetPositionIndex = 4, TargetBlackBodyTemperature = 50.0f }
            }
        };

        Console.WriteLine($"[E2E] Recipe '{recipe.Name}' - {recipe.Steps.Count} steps");
        Console.WriteLine($"[E2E] Phase: chamber stabilization (target T={recipe.GlobalTargetTemperature}, H={recipe.GlobalTargetHumidity})");

        try
        {
            await plc.StartChamberAsync();
            await plc.SetTargetTemperatureAsync(recipe.GlobalTargetTemperature);
            await plc.SetTargetHumidityAsync(recipe.GlobalTargetHumidity);
            await WaitUntilAsync(async () =>
                Math.Abs(await plc.GetCurrentTemperatureAsync() - recipe.GlobalTargetTemperature) <= 0.5f &&
                Math.Abs(await plc.GetCurrentHumidityAsync() - recipe.GlobalTargetHumidity) <= 0.5f,
                TimeSpan.FromSeconds(timeoutSec));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[E2E] FAIL - PLC chamber phase: {ex.Message}");
            return ExitPlcOperationFailed;
        }

        for (int i = 0; i < recipe.Steps.Count; i++)
        {
            var step = recipe.Steps[i];
            Console.WriteLine($"[E2E] Step {i+1}/{recipe.Steps.Count}: cam={step.CameraIndex}, pos={step.TargetPositionIndex}, BBtemp={step.TargetBlackBodyTemperature}");

            try
            {
                await plc.SetPointCoordinateAsync(step.TargetPositionIndex, 100 + step.TargetPositionIndex, 200 + step.TargetPositionIndex);
                await plc.MoveServoToPositionAsync(step.TargetPositionIndex);
                if (externalMode)
                    await WaitUntilAsync(async () => !await plc.IsServoAtPositionAsync(step.TargetPositionIndex), TimeSpan.FromSeconds(1));
                await WaitUntilAsync(async () => await plc.IsServoAtPositionAsync(step.TargetPositionIndex), TimeSpan.FromSeconds(timeoutSec));

                await blackBody.SetTemperatureAsync(step.CameraIndex, step.TargetBlackBodyTemperature);
                await WaitUntilAsync(async () =>
                    Math.Abs(await blackBody.GetCurrentTemperatureAsync(step.CameraIndex) - step.TargetBlackBodyTemperature) <= 0.5f,
                    TimeSpan.FromSeconds(timeoutSec));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[E2E] FAIL - PLC step {i + 1}: {ex.Message}");
                await StopChamberQuietlyAsync(plc);
                return ExitPlcOperationFailed;
            }

            var tcs = new TaskCompletionSource<CaptureResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters[step.StepId] = tcs;

            string targetAgent = $"Agent_{step.CameraIndex}";
            Console.WriteLine($"[E2E]   -> publish capture cmd to {targetAgent}");
            await nats.PublishCaptureCommandAsync(new CaptureCommandMessage
            {
                TargetAgentId = targetAgent,
                RecipeStepId  = step.StepId,
                Timestamp     = DateTime.UtcNow
            });

            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
            if (done != tcs.Task)
            {
                Console.Error.WriteLine($"[E2E] FAIL - step {i+1} capture timeout ({timeoutSec}s). Agent '{targetAgent}' not responding.");
                await StopChamberQuietlyAsync(plc);
                return ExitCameraTimeout;
            }
        }

        await StopChamberQuietlyAsync(plc);

        Console.WriteLine();
        Console.WriteLine("[E2E] === VERIFICATION ===");
        Console.WriteLine($"[E2E] Captures received: {captures.Count} / {recipe.Steps.Count}");

        int agent0 = 0, agent1 = 0;
        int filesExist = 0, filesMissing = 0;
        foreach (var c in captures)
        {
            if (c.AgentId == "Agent_0") agent0++;
            if (c.AgentId == "Agent_1") agent1++;
            if (!string.IsNullOrEmpty(c.ImagePath) && File.Exists(c.ImagePath))
            {
                var info = new FileInfo(c.ImagePath);
                Console.WriteLine($"[E2E]   OK file: {c.ImagePath} ({info.Length} bytes)");
                filesExist++;
            }
            else
            {
                Console.WriteLine($"[E2E]   MISSING file: {c.ImagePath}");
                filesMissing++;
            }
        }

        Console.WriteLine($"[E2E] Agent_0 captures: {agent0}, Agent_1 captures: {agent1}");
        Console.WriteLine($"[E2E] Image files: {filesExist} present, {filesMissing} missing");

        bool pass = captures.Count == recipe.Steps.Count
                 && agent0 == 2 && agent1 == 2
                 && filesMissing == 0;

        if (externalMode)
            pass = pass && await ExternalStateLooksDoneAsync(plc, blackBody);

        Console.WriteLine();
        Console.WriteLine(pass ? "[E2E] *** PASS ***" : "[E2E] *** FAIL ***");
        return pass ? ExitPass : ExitVerificationFailed;
    }

    private static async Task<bool> ExternalStateLooksDoneAsync(IPlcController plc, IBlackBodyController blackBody)
    {
        PlcStatusSnapshot status = await plc.ReadStatusAsync();
        bool plcDone = Math.Abs(status.CurrentTemperature - 30.0f) <= 0.5f
                    && Math.Abs(status.CurrentHumidity - 55.0f) <= 0.5f
                    && status.CurrentPoint == 4
                    && !status.ServoXBusy
                    && !status.ServoYBusy;
        bool bbDone = Math.Abs(await blackBody.GetCurrentTemperatureAsync(0) - 45.0f) <= 0.5f
                   && Math.Abs(await blackBody.GetCurrentTemperatureAsync(1) - 50.0f) <= 0.5f;

        Console.WriteLine($"[E2E] Final PLC: T={status.CurrentTemperature:F1}, H={status.CurrentHumidity:F1}, point={status.CurrentPoint}, busy={status.ServoXBusy || status.ServoYBusy}");
        return plcDone && bbDone;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not met before the deadline.");
    }

    private static async Task StopChamberQuietlyAsync(IPlcController plc)
    {
        try { await plc.StopChamberAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[E2E] WARN - chamber stop failed: {ex.Message}"); }
    }
}
