using System.Net;
using System.Text.Json;

namespace HeatingCameraSystem.Simulator.Config;

/// <summary>Thrown by <see cref="SimulatorSettings.Load"/> when a config value is invalid.
/// The message always NAMES the offending property so operators can fix the JSON.</summary>
public sealed class SimulatorSettingsException : Exception
{
    public SimulatorSettingsException(string message) : base(message) { }
    public SimulatorSettingsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>FEnet listen endpoint + NATS URL. Listen defaults to loopback:2004.</summary>
public sealed record EndpointSettings(
    string ListenAddress = "127.0.0.1",
    int ListenPort = 2004,
    string NatsUrl = "nats://127.0.0.1:4222");

/// <summary>Deterministic dynamics — tick cadence and physical ramp rates. Never random.</summary>
public sealed record DynamicsSettings(
    int TickMs = 100,
    double TemperatureRatePerSecond = 20.0,
    double HumidityRatePerSecond = 40.0,
    double BlackbodyRatePerSecond = 30.0,
    int ServoBusyMs = 500,
    int HeartbeatSeconds = 5,
    int LiveFrameIntervalMs = 100);

/// <summary>Simulated live-frame geometry. Width/height must be positive AND even.</summary>
public sealed record FrameSettings(int Width = 640, int Height = 480);

/// <summary>One simulated camera identity (mirrors <c>CameraDescriptor</c> AgentId/index shape).</summary>
public sealed record CameraSettings(string AgentId, int CameraIndex);

/// <summary>
/// Validated, immutable simulator configuration. <see cref="Load"/> reads JSON
/// (System.Text.Json), or creates-with-defaults when the file is missing
/// (AgentUiConfig precedent). Malformed JSON is surfaced as an actionable
/// <see cref="SimulatorSettingsException"/> rather than swallowed.
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

    /// <summary>Output lives under the simulator base dir — never %LOCALAPPDATA%.</summary>
    public static string DefaultOutputPath => Path.Combine(AppContext.BaseDirectory, "ImageStorage");

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

    public static SimulatorSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new SimulatorSettingsException("path must be a non-empty file path.");

        if (!File.Exists(path))
        {
            // AgentUiConfig precedent: a missing file is created with defaults and persisted.
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
            // Diverge from AgentUiConfig swallow-and-log: name where the JSON is bad.
            string where = string.IsNullOrEmpty(ex.Path) ? "document root" : ex.Path;
            throw new SimulatorSettingsException(
                $"Simulator settings file '{path}' contains invalid JSON at {where}: {ex.Message}", ex);
        }

        settings.Validate();
        return settings;
    }

    /// <summary>Throws <see cref="SimulatorSettingsException"/> naming the first invalid property.</summary>
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

    private static SimulatorSettingsException Bad(string property, string reason) =>
        new($"{property} {reason}");
}
