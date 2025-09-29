using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class TranslationPipelineTests
{
    [Fact]
    public async Task ReturnsCachedResultForDuplicateRequest()
    {
        var options = Options.Create(new PluginOptions
        {
            DailyBudgetUsd = 0.2m,
            CacheTtl = TimeSpan.FromHours(1),
            RequestsPerMinute = 10,
            MaxConcurrentTranslations = 2,
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", CostPerCharUsd = 0.1m, Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso" }
            }
        });

        var pipeline = BuildPipeline(options);

        var request = new TranslationRequest
        {
            Text = "hi",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        };

        var first = await pipeline.ExecuteAsync(request, CancellationToken.None);
        var second = await pipeline.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(first.TranslatedText, second.TranslatedText);
    }

    [Fact]
    public async Task ThrowsWhenRateLimitExceeded()
    {
        var options = Options.Create(new PluginOptions
        {
            RequestsPerMinute = 1,
            MaxConcurrentTranslations = 1,
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso" }
            }
        });

        var pipeline = BuildPipeline(options);

        await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "first",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None);

        await Assert.ThrowsAsync<RateLimitExceededException>(() => pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "second",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));
    }

    private static TranslationPipeline BuildPipeline(IOptions<PluginOptions> options)
    {
        var glossary = new GlossaryService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), options);
        var cache = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        return new TranslationPipeline(router, glossary, new OfflineDraftStore(options), new LanguageDetector(), cache, throttle, options);
    }
}
