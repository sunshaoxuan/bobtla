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

        Assert.Equal("phase4", snapshot.CurrentStageId);
        Assert.Contains(snapshot.Stages, stage => stage.Id == "phase4" && !stage.Completed);
        Assert.Equal(3, snapshot.NextSteps.Count);
        Assert.Contains(snapshot.NextSteps, step => step.Contains("前端"));
        Assert.Equal(60, snapshot.OverallCompletionPercent);
        Assert.True(snapshot.Frontend.DataPlaneReady);
        Assert.True(snapshot.Frontend.UiImplemented);
        Assert.False(snapshot.Frontend.IntegrationReady);
        Assert.Equal(55, snapshot.Frontend.CompletionPercent);
    }
}
