using System.Collections.Generic;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ConfigurationSummaryServiceTests
{
    [Fact]
    public void CreateSummary_Returns_Providers_And_Tone_Info()
    {
        var options = Options.Create(new PluginOptions
        {
            MaxCharactersPerRequest = 1234,
            DailyBudgetUsd = 55m,
            RequestsPerMinute = 80,
            MaxConcurrentTranslations = 3,
            SupportedLanguages = new List<string> { "ja-JP", "en-US" },
            DefaultTargetLanguages = new List<string> { "ja-JP" },
            Providers =
            {
                new ModelProviderOptions
                {
                    Id = "openai",
                    Kind = ModelProviderKind.OpenAi,
                    Reliability = 0.97,
                    CostPerCharUsd = 0.00003m,
                    LatencyTargetMs = 500,
                    Regions = { "global" },
                    Certifications = { "iso27001" }
                }
            }
        });

        var toneService = new ToneTemplateService();
        var glossary = new GlossaryService();
        glossary.LoadEntries(new[]
        {
            new GlossaryEntry("CPU", "中央処理装置", "tenant:contoso")
        });

        var service = new ConfigurationSummaryService(options, toneService, glossary);

        var summary = service.CreateSummary();

        Assert.Equal(1234, summary.MaxCharactersPerRequest);
        Assert.Equal(55m, summary.DailyBudgetUsd);
        Assert.Equal(80, summary.RequestsPerMinute);
        Assert.Equal(3, summary.MaxConcurrentTranslations);
        Assert.Contains("ja-JP", summary.SupportedLanguages);
        Assert.Single(summary.DefaultTargetLanguages);
        Assert.Single(summary.Providers);
        Assert.Equal(ModelProviderKind.OpenAi, summary.Providers[0].Kind);
        Assert.Equal(1, summary.GlossaryEntryCount);
        Assert.Contains(ToneTemplateService.Business, summary.ToneTemplates.Keys);
    }

    [Fact]
    public void CreateSummary_ExposesExpandedLanguageList()
    {
        var options = Options.Create(new PluginOptions());
        var service = new ConfigurationSummaryService(options, new ToneTemplateService(), new GlossaryService());

        var summary = service.CreateSummary();

        Assert.True(summary.SupportedLanguages.Count >= 30);
        Assert.Contains("de-DE", summary.SupportedLanguages);
        Assert.Contains("ar-SA", summary.SupportedLanguages);
        Assert.Contains("vi-VN", summary.SupportedLanguages);
    }
}
