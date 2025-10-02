using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ProjectStatusServiceTests
{
    [Fact]
    public void GetSnapshot_Returns_CurrentStage_And_NextSteps()
    {
        var service = new ProjectStatusService();

        var snapshot = service.GetSnapshot();

        Assert.Equal("phase5", snapshot.CurrentStageId);
        Assert.Contains(snapshot.Stages, stage => stage.Id == "phase4" && stage.Completed);
        Assert.Equal(3, snapshot.NextSteps.Count);
        Assert.Contains(snapshot.NextSteps, step => step.Contains("密钥"));
        Assert.Equal(80, snapshot.OverallCompletionPercent);
        Assert.True(snapshot.Frontend.DataPlaneReady);
        Assert.True(snapshot.Frontend.UiImplemented);
        Assert.True(snapshot.Frontend.IntegrationReady);
        Assert.Equal(80, snapshot.Frontend.CompletionPercent);
    }
}
