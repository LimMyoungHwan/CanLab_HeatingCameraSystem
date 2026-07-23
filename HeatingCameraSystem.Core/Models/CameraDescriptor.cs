namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Identity + physical binding of one local camera hosted by an AgentUI process.
    /// <see cref="AgentId"/> is the stable logical NATS identity (see BuildAgentId);
    /// <see cref="OpenCvIndex"/> is the physical OpenCV/DirectShow device index.
    /// </summary>
    public sealed record CameraDescriptor(string AgentId, int OpenCvIndex, string Alias, string? SerialPortName = null);
}
