using System.Net;
using System.Text.Json;

namespace HeatingCameraSystem.Simulator.Config;

/// <summary><see cref="SimulatorSettings.Load"/>가 잘못된 설정값을 만나면 던진다.
/// 메시지에 항상 문제가 된 프로퍼티 이름을 담아 운영자가 JSON을 고칠 수 있게 한다.</summary>
public sealed class SimulatorSettingsException : Exception
{
    public SimulatorSettingsException(string message) : base(message) { }
    public SimulatorSettingsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>FEnet listen 엔드포인트 + NATS URL. listen 기본값은 loopback:2004다.</summary>
public sealed record EndpointSettings(
    string ListenAddress = "127.0.0.1",
    int ListenPort = 2004,
    string NatsUrl = "nats://127.0.0.1:4222");

/// <summary>결정론적 동역학 설정 — tick 주기와 물리 램프 속도. 난수는 절대 쓰지 않는다.</summary>
public sealed record DynamicsSettings(
    int TickMs = 100,
    double TemperatureRatePerSecond = 20.0,
    double HumidityRatePerSecond = 40.0,
    double BlackbodyRatePerSecond = 30.0,
    int ServoBusyMs = 500,
    int HeartbeatSeconds = 5,
    int LiveFrameIntervalMs = 100,
    // 조그 램프 속도(mm/s). 기존 위치 기반 생성 호출이 깨지지 않도록 마지막에 둔다.
    double JogRatePerSecond = 50.0);

/// <summary>시뮬레이션 라이브 프레임 크기. 가로/세로 모두 양수이면서 짝수여야 한다.</summary>
public sealed record FrameSettings(int Width = 640, int Height = 480);

/// <summary>시뮬레이션 카메라 1대의 신원(<c>CameraDescriptor</c>의 AgentId/index 형태를 그대로 따른다).</summary>
public sealed record CameraSettings(string AgentId, int CameraIndex);

/// <summary>
/// 검증 완료된 불변 Simulator 설정. <see cref="Load"/>가 JSON(System.Text.Json)을 읽고,
/// 파일이 없으면 기본값으로 생성해 저장한다(AgentUiConfig 선례). 깨진 JSON은 삼키지 않고
/// 조치 가능한 <see cref="SimulatorSettingsException"/>으로 드러낸다.
/// </summary>
public sealed record SimulatorSettings(
    EndpointSettings Endpoint,
    DynamicsSettings Dynamics,
    FrameSettings Frame,
    string OutputPath,
    IReadOnlyList<CameraSettings> Cameras)
{
    private const int MaxCameras = 64;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>출력은 Simulator 기준 디렉터리 아래에 둔다 — %LOCALAPPDATA%는 절대 쓰지 않는다.</summary>
    public static string DefaultOutputPath => Path.Combine(AppContext.BaseDirectory, "ImageStorage");

    /// <summary>기본 구성: loopback:2004 + 카메라 2대(<c>Agent_0</c>, <c>Agent_1</c>).</summary>
    public static SimulatorSettings CreateDefaults() => new(
        new EndpointSettings(),
        new DynamicsSettings(),
        new FrameSettings(),
        DefaultOutputPath,
        new[]
        {
            new CameraSettings("Agent_0", 0),
            new CameraSettings("Agent_1", 1),
        });

    /// <summary>JSON 파일을 읽어 검증까지 마친 설정을 돌려준다. 파일이 없으면 기본값을 만들어 저장한다.</summary>
    public static SimulatorSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new SimulatorSettingsException("path must be a non-empty file path.");

        if (!File.Exists(path))
        {
            // AgentUiConfig 선례: 파일이 없으면 기본값으로 생성해 저장한다.
            SimulatorSettings defaults = CreateDefaults();
            defaults.Validate();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOpts));
            return defaults;
        }

        string json = File.ReadAllText(path);
        SimulatorSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<SimulatorSettings>(json, JsonOpts)
                ?? throw new SimulatorSettingsException($"Simulator settings file '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            // AgentUiConfig의 삼키고-로그 방식과는 다르게 간다: JSON의 어디가 깨졌는지 이름으로 알린다.
            string where = string.IsNullOrEmpty(ex.Path) ? "document root" : ex.Path;
            throw new SimulatorSettingsException(
                $"Simulator settings file '{path}' contains invalid JSON at {where}: {ex.Message}", ex);
        }

        settings = settings.NormalizeOutputPath(path);
        settings.Validate();
        return settings;
    }

    /// <summary>상대 <see cref="OutputPath"/>를 설정 파일 디렉터리 기준으로 풀어,
    /// 호출자의 작업 디렉터리와 무관하게 캡처가 항상 같은 곳에 떨어지게 한다.</summary>
    private SimulatorSettings NormalizeOutputPath(string configPath)
    {
        if (string.IsNullOrWhiteSpace(OutputPath) || Path.IsPathRooted(OutputPath))
            return this;
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? AppContext.BaseDirectory;
        return this with { OutputPath = Path.GetFullPath(Path.Combine(baseDir, OutputPath)) };
    }

    /// <summary>처음 발견한 잘못된 프로퍼티 이름을 담아 <see cref="SimulatorSettingsException"/>을 던진다.</summary>
    public void Validate()
    {
        if (Endpoint is null) throw Bad(nameof(Endpoint), "is required.");
        if (Dynamics is null) throw Bad(nameof(Dynamics), "is required.");
        if (Frame is null) throw Bad(nameof(Frame), "is required.");
        if (Cameras is null) throw Bad(nameof(Cameras), "is required.");
        if (string.IsNullOrWhiteSpace(OutputPath)) throw Bad(nameof(OutputPath), "must be a non-empty path.");

        if (!IPAddress.TryParse(Endpoint.ListenAddress, out IPAddress? ip) || !IPAddress.IsLoopback(ip))
            throw Bad(nameof(Endpoint.ListenAddress), $"'{Endpoint.ListenAddress}' must be a loopback address.");
        if (Endpoint.ListenPort is < 1 or > 65535)
            throw Bad(nameof(Endpoint.ListenPort), $"{Endpoint.ListenPort} must be in 1-65535.");
        if (string.IsNullOrWhiteSpace(Endpoint.NatsUrl))
            throw Bad(nameof(Endpoint.NatsUrl), "must be a non-empty URL.");

        RequirePositive(Dynamics.TickMs, nameof(Dynamics.TickMs));
        RequirePositive(Dynamics.TemperatureRatePerSecond, nameof(Dynamics.TemperatureRatePerSecond));
        RequirePositive(Dynamics.HumidityRatePerSecond, nameof(Dynamics.HumidityRatePerSecond));
        RequirePositive(Dynamics.BlackbodyRatePerSecond, nameof(Dynamics.BlackbodyRatePerSecond));
        RequirePositive(Dynamics.JogRatePerSecond, nameof(Dynamics.JogRatePerSecond));
        RequirePositive(Dynamics.ServoBusyMs, nameof(Dynamics.ServoBusyMs));
        RequirePositive(Dynamics.HeartbeatSeconds, nameof(Dynamics.HeartbeatSeconds));
        RequirePositive(Dynamics.LiveFrameIntervalMs, nameof(Dynamics.LiveFrameIntervalMs));

        RequireEvenPositive(Frame.Width, nameof(Frame.Width));
        RequireEvenPositive(Frame.Height, nameof(Frame.Height));

        if (Cameras.Count is < 1 or > MaxCameras)
            throw Bad(nameof(Cameras), $"count {Cameras.Count} must be 1-{MaxCameras}.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var indices = new HashSet<int>();
        foreach (CameraSettings cam in Cameras)
        {
            if (string.IsNullOrWhiteSpace(cam.AgentId))
                throw Bad(nameof(cam.AgentId), "must be non-blank.");
            if (!IsValidSubjectToken(cam.AgentId))
                throw Bad(nameof(cam.AgentId), $"'{cam.AgentId}' must be a single NATS subject token ([A-Za-z0-9_-]).");
            if (!ids.Add(cam.AgentId))
                throw Bad(nameof(cam.AgentId), $"'{cam.AgentId}' is duplicated.");
            if (cam.CameraIndex < 0)
                throw Bad(nameof(cam.CameraIndex), $"{cam.CameraIndex} must be non-negative.");
            if (!indices.Add(cam.CameraIndex))
                throw Bad(nameof(cam.CameraIndex), $"{cam.CameraIndex} is duplicated.");
        }

        RequireWritable(OutputPath, nameof(OutputPath));
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0) throw Bad(name, $"{value} must be > 0.");
    }

    private static void RequirePositive(double value, string name)
    {
        if (value <= 0 || double.IsNaN(value)) throw Bad(name, $"{value} must be > 0.");
    }

    private static void RequireEvenPositive(int value, string name)
    {
        if (value <= 0) throw Bad(name, $"{value} must be > 0.");
        if (value % 2 != 0) throw Bad(name, $"{value} must be even.");
    }

    private static void RequireWritable(string path, string name)
    {
        try
        {
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, ".sim_write_probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new SimulatorSettingsException($"{name} '{path}' is not writable: {ex.Message}", ex);
        }
    }

    private static bool IsValidSubjectToken(string id)
    {
        foreach (char c in id)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) return false;
        return true;
    }

    private static SimulatorSettingsException Bad(string property, string reason) =>
        new($"{property} {reason}");
}
