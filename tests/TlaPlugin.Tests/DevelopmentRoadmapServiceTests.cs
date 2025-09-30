using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class DevelopmentRoadmapServiceTests
{
    [Fact]
    public void GetRoadmap_Returns_ActiveStage_And_TestCatalog()
    {
        var service = new DevelopmentRoadmapService();

        var roadmap = service.GetRoadmap();

        Assert.Equal("phase4", roadmap.ActiveStageId);
        Assert.Equal(5, roadmap.Stages.Count);
        Assert.Contains(roadmap.Stages, stage => stage.Id == "phase4" && !stage.Completed);
        Assert.Contains(roadmap.Stages, stage => stage.Deliverables.Contains("新增 src/webapp 仪表盘页面与视图模型"));
        Assert.Contains(roadmap.Tests, test => test.Id == "dashboard" && test.Automated);
    }
}
