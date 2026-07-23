namespace HeatingCameraSystem.Core.Models
{
    /// <summary>
    /// Lifecycle state of a single-camera runtime.
    /// </summary>
    public enum CameraRuntimeStatus
    {
        Stopped,
        Running,
        Faulted
    }
}
