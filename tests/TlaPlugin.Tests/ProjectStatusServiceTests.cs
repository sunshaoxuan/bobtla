using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ProjectStatusServiceTests
{
    [Fact]
    public void GetSnapshot_Reflects_DefaultStageFivePending_WhenPrerequisitesMissing()
    {
        var store = new InMemoryStageReadinessStore();
        var options = Options.Create(new PluginOptions());
        var metrics = new UsageMetricsService(store);
        var roadmap = new DevelopmentRoadmapService();
        var service = new ProjectStatusService(options, metrics, roadmap, store);

        var snapshot = service.GetSnapshot();

        Assert.Equal("phase5", snapshot.CurrentStageId);
        Assert.Contains(snapshot.Stages, stage => stage.Id == "phase4" && stage.Completed);
        Assert.Equal(3, snapshot.NextSteps.Count);
        Assert.Contains(snapshot.NextSteps, step => step.Contains("密钥"));
        Assert.False(snapshot.Stages.Single(stage => stage.Id == "phase5").Completed);
        Assert.Equal(80, snapshot.OverallCompletionPercent);
        Assert.True(snapshot.Frontend.DataPlaneReady);
        Assert.True(snapshot.Frontend.UiImplemented);
        Assert.False(snapshot.Frontend.IntegrationReady);
        Assert.Equal(80, snapshot.Frontend.CompletionPercent);
    }

    [Fact]
    public void GetSnapshot_MarksStageFiveComplete_WhenPersistenceIndicatesRecentSuccess()
    {
        var pluginOptions = new PluginOptions
        {
            Security = new SecurityOptions
            {
                UseHmacFallback = false,
                GraphScopes = new[] { "https://graph.microsoft.com/.default", "https://graph.microsoft.com/ChannelMessage.Send" }
            }
        };
        var store = new InMemoryStageReadinessStore();
        var options = Options.Create(pluginOptions);
        var metrics = new UsageMetricsService(store);
        var roadmap = new DevelopmentRoadmapService();
        store.Seed(DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(1)));
        var service = new ProjectStatusService(options, metrics, roadmap, store);

        var snapshot = service.GetSnapshot();

        var stageFive = snapshot.Stages.Single(stage => stage.Id == "phase5");
        Assert.True(stageFive.Completed);
        Assert.Equal(100, snapshot.OverallCompletionPercent);
        Assert.True(snapshot.Frontend.IntegrationReady);
        Assert.Equal(100, snapshot.Frontend.CompletionPercent);
    }

    [Fact]
    public void GetSnapshot_FallsBackToMetrics_WhenPersistenceMissing()
    {
        var pluginOptions = new PluginOptions
        {
            Security = new SecurityOptions
            {
                UseHmacFallback = false,
                GraphScopes = new[] { "https://graph.microsoft.com/.default" }
            }
        };
        var store = new InMemoryStageReadinessStore();
        var metrics = new UsageMetricsService(store);
        var options = Options.Create(pluginOptions);
        metrics.RecordSuccess("contoso", "model-a", 0.12m, 120, 1);
        var roadmap = new DevelopmentRoadmapService();
        store.Seed(DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(30)));
        var service = new ProjectStatusService(options, metrics, roadmap, store);

        var snapshot = service.GetSnapshot();

        Assert.True(snapshot.Stages.Single(stage => stage.Id == "phase5").Completed);
        Assert.Equal(100, snapshot.OverallCompletionPercent);
        Assert.True(snapshot.Frontend.IntegrationReady);
        Assert.Equal(100, snapshot.Frontend.CompletionPercent);
    }

    [Fact]
    public void GetSnapshot_RecognizesRecentSuccess_WhenMetricsRecordWritesPersistence()
    {
        var pluginOptions = new PluginOptions
        {
            Security = new SecurityOptions
            {
                UseHmacFallback = false,
                GraphScopes = new[] { "https://graph.microsoft.com/.default" }
            }
        };

        var store = new InMemoryStageReadinessStore();
        var metrics = new UsageMetricsService(store);
        var options = Options.Create(pluginOptions);
        var roadmap = new DevelopmentRoadmapService();
        var service = new ProjectStatusService(options, metrics, roadmap, store);

        metrics.RecordSuccess("contoso", "model-a", 0.42m, 200, 1);

        Assert.NotNull(store.LastSuccess);

        var snapshot = service.GetSnapshot();

        Assert.True(snapshot.StageFiveDiagnostics.SmokeTestRecent);
        Assert.True(snapshot.StageFiveDiagnostics.StageReady);
        Assert.Equal(store.LastSuccess, snapshot.StageFiveDiagnostics.LastSmokeSuccess);
    }

    [Fact]
    public void RecordSuccess_WhenPersistenceWriteFails_LogsWarning()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tla-plugin-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "locked.txt");
        File.WriteAllText(filePath, string.Empty);

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var logger = new TestLogger<FileStageReadinessStore>();
            var store = new FileStageReadinessStore(filePath, logger);
            var metrics = new UsageMetricsService(store);

            metrics.RecordSuccess("contoso", "model-a", 0.42m, 200, 1);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.NotNull(entry.Exception);
            Assert.Contains(filePath, entry.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
