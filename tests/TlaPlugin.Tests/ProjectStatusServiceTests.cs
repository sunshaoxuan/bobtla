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

        Assert.Equal("stage8", snapshot.CurrentStageId);
        Assert.Contains(snapshot.Stages, stage => stage.Id == "stage9" && !stage.Completed);
        Assert.Equal(4, snapshot.NextSteps.Count);
        Assert.Contains(snapshot.NextSteps, step => step.Contains("前端"));
    }
}
