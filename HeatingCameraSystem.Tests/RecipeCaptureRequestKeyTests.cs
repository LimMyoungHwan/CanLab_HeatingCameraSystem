using HeatingCameraSystem.Core.Models;
using Xunit;

namespace HeatingCameraSystem.Tests;

public class RecipeCaptureRequestKeyTests
{
    [Theory]
    [InlineData("Agent_7", 7)]
    [InlineData("Agent_A:Bay_1", 0)]
    public void SeparateRuns_PreserveHistoryStepAndCameraRoundIdentity(string agentId, int cameraIndex)
    {
        string stepId = Guid.NewGuid().ToString();
        string firstRun = RecipeCaptureRequestKey.Create(stepId, agentId, cameraIndex, 3, "first-run");
        string nextRun = RecipeCaptureRequestKey.Create(stepId, agentId, cameraIndex, 3, "next-run");

        Assert.NotEqual(firstRun, nextRun);
        Assert.Equal(stepId, RecipeCaptureRequestKey.GetStepId(firstRun));
        Assert.Equal(stepId, RecipeCaptureRequestKey.GetStepId(nextRun));
        Assert.StartsWith($"{stepId}:{agentId}:{cameraIndex}:", firstRun);
        Assert.EndsWith(":3", firstRun);
        Assert.EndsWith(":3", nextRun);
    }
}
