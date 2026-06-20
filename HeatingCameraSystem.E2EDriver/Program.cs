using System.Collections.Concurrent;
using HeatingCameraSystem.Core.Models;
using HeatingCameraSystem.Protocols;
using HeatingCameraSystem.Protocols.Simulation;

namespace HeatingCameraSystem.E2EDriver;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string natsUrl = args.Length > 0 ? args[0] : "nats://127.0.0.1:4222";
        int    timeoutSec = args.Length > 1 && int.TryParse(args[1], out var t) ? t : 30;

        Console.WriteLine($"[E2E] NATS = {natsUrl}, capture timeout = {timeoutSec}s");

        var plc = new FakePlcController();
        await plc.ConnectAsync("any");

        await using var nats = new NatsCommunicationService();
        try
        {
            await nats.ConnectAsync(natsUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[E2E] FAIL — NATS connect: {ex.Message}");
            return 2;
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

        Console.WriteLine($"[E2E] Recipe '{recipe.Name}' — {recipe.Steps.Count} steps");
        Console.WriteLine($"[E2E] Phase: chamber stabilization (target T={recipe.GlobalTargetTemperature}, H={recipe.GlobalTargetHumidity})");

        await plc.StartChamberAsync();
        await plc.SetTargetTemperatureAsync(recipe.GlobalTargetTemperature);
        await plc.SetTargetHumidityAsync(recipe.GlobalTargetHumidity);

        for (int i = 0; i < recipe.Steps.Count; i++)
        {
            var step = recipe.Steps[i];
            Console.WriteLine($"[E2E] Step {i+1}/{recipe.Steps.Count}: cam={step.CameraIndex}, pos={step.TargetPositionIndex}, BBtemp={step.TargetBlackBodyTemperature}");

            await plc.MoveServoToPositionAsync(step.TargetPositionIndex);
            while (!await plc.IsServoAtPositionAsync()) await Task.Delay(100);

            await plc.SetBlackBodyTemperatureAsync(0, step.TargetBlackBodyTemperature);
            while (Math.Abs(await plc.GetCurrentBlackBodyTemperatureAsync(0) - step.TargetBlackBodyTemperature) > 0.5f)
                await Task.Delay(100);

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
                Console.Error.WriteLine($"[E2E] FAIL — step {i+1} capture timeout ({timeoutSec}s). Agent '{targetAgent}' not responding.");
                await plc.StopChamberAsync();
                return 3;
            }
        }

        await plc.StopChamberAsync();

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

        Console.WriteLine();
        Console.WriteLine(pass ? "[E2E] *** PASS ***" : "[E2E] *** FAIL ***");
        return pass ? 0 : 1;
    }
}
