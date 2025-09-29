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
        Assert.Empty(contoso.Failures);

        var overall = report.Overall;
        Assert.Equal(4, overall.Translations);
        Assert.Equal(2.2m, overall.TotalCostUsd);
        Assert.Equal(300d, overall.AverageLatencyMs);
        Assert.Empty(overall.Failures);
    }

    [Fact]
    public void RecordsFailuresPerTenantAndAggregates()
    {
        var service = new UsageMetricsService();
        service.RecordFailure("contoso", UsageMetricsService.FailureReasons.Compliance);
        service.RecordFailure("contoso", UsageMetricsService.FailureReasons.Compliance);
        service.RecordFailure("contoso", UsageMetricsService.FailureReasons.Budget);
        service.RecordFailure("fabrikam", UsageMetricsService.FailureReasons.Provider);

        var report = service.GetReport();
        var contoso = report.Tenants.Single(snapshot => snapshot.TenantId == "contoso");
        var compliance = contoso.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Compliance);
        Assert.Equal(2, compliance.Count);
        var budget = contoso.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Budget);
        Assert.Equal(1, budget.Count);

        var fabrikam = report.Tenants.Single(snapshot => snapshot.TenantId == "fabrikam");
        var provider = fabrikam.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Provider);
        Assert.Equal(1, provider.Count);

        var overall = report.Overall;
        var overallCompliance = overall.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Compliance);
        Assert.Equal(2, overallCompliance.Count);
        var overallBudget = overall.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Budget);
        Assert.Equal(1, overallBudget.Count);
        var overallProvider = overall.Failures.Single(failure => failure.Reason == UsageMetricsService.FailureReasons.Provider);
        Assert.Equal(1, overallProvider.Count);
    }
}
