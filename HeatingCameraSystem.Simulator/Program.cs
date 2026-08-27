using System.Net.Sockets;
using HeatingCameraSystem.Simulator;
using HeatingCameraSystem.Simulator.Config;

// Simulator 콘솔 진입점. 첫 번째 비옵션 인수를 설정 파일 경로로 쓰고(기본: exe 폴더의 simulator.json),
// --plc-only가 있으면 NATS 카메라 없이 PLC만 기동한다.
// 종료 코드: 0 정상 종료, 2 설정 오류, 3 FEnet 바인드/시작 실패, 4 NATS 연결/시작 실패.
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
