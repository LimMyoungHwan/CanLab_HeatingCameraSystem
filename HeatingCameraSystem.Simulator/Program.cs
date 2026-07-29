using System.Net.Sockets;
using HeatingCameraSystem.Simulator;
using HeatingCameraSystem.Simulator.Config;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    string? pathArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
    string path = pathArg ?? Path.Combine(AppContext.BaseDirectory, "simulator.json");
    bool plcOnly = args.Any(a => a.Equals("--plc-only", StringComparison.OrdinalIgnoreCase));
    SimulatorSettings settings;
    try
    {
        settings = SimulatorSettings.Load(path);
    }
    catch (SimulatorSettingsException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    await using var host = new SimulatorHost(settings, startCameras: !plcOnly);
    try
    {
        await host.StartAsync();
    }
    catch (SocketException ex)
    {
        Console.Error.WriteLine($"FEnet bind/start failed: {ex.Message}");
        return 3;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"NATS connect/start failed: {ex.Message}");
        return 4;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    try
    {
        await host.RunConsoleAsync(cts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        return 0;
    }
}
