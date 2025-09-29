using System.Linq;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class UsageMetricsServiceTests
{
    [Fact]
    public void AggregatesMetricsPerTenantAndModel()
    {
        var service = new UsageMetricsService();
        service.RecordSuccess("contoso", "openai", 0.5m, 200, 1);
        service.RecordSuccess("contoso", "openai", 0.5m, 300, 2);
        service.RecordSuccess("fabrikam", "groq", 1.2m, 400, 1);

        var report = service.GetReport();
        Assert.Equal(2, report.Tenants.Count);

        var contoso = report.Tenants.Single(t => t.TenantId == "contoso");
        Assert.Equal(3, contoso.Translations);
        Assert.Equal(1.0m, contoso.TotalCostUsd);
        Assert.Equal(266.67d, contoso.AverageLatencyMs, 2);
        var model = contoso.Models.Single();
        Assert.Equal("openai", model.ModelId);
        Assert.Equal(3, model.Translations);
        Assert.Equal(1.0m, model.TotalCostUsd);

        var overall = report.Overall;
        Assert.Equal(4, overall.Translations);
        Assert.Equal(2.2m, overall.TotalCostUsd);
        Assert.Equal(300d, overall.AverageLatencyMs);
    }
}
