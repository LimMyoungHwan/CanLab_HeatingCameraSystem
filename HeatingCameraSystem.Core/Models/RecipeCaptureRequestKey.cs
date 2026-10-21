namespace HeatingCameraSystem.Core.Models;

/// <summary>Capture correlation key: StepId:AgentId:CameraIndex:round. AgentId may contain colons.</summary>
public static class RecipeCaptureRequestKey
{
    public static string Create(string stepId, string agentId, int cameraIndex, int round) =>
        $"{stepId}:{agentId}:{cameraIndex}:{round}";

    /// <summary>Accepts GUID step IDs, or exact known legacy step IDs, with or without a request suffix.</summary>
    public static string? GetStepId(string? requestKey, IEnumerable<RecipeStep>? knownSteps = null)
    {
        if (string.IsNullOrWhiteSpace(requestKey)) return null;
        int separator = requestKey.IndexOf(':');
        string stepId = separator < 0 ? requestKey : requestKey[..separator];
        if (stepId.Length == 0) return null;
        return Guid.TryParse(stepId, out _) || knownSteps?.Any(step => step.StepId == stepId) == true
            ? stepId : null;
    }
}
