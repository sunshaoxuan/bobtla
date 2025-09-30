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

        Assert.Equal("stage9", roadmap.ActiveStageId);
        Assert.Equal(10, roadmap.Stages.Count);
        Assert.Contains(roadmap.Stages, stage => stage.Id == "stage9" && !stage.Completed);
        Assert.Contains(roadmap.Stages, stage => stage.Deliverables.Contains("新增 /api/roadmap 提供阶段成果与测试摘要"));
        Assert.Contains(roadmap.Tests, test => test.Id == "roadmap" && test.Automated);
    }
}
