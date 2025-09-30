using System;
using System.Collections.Generic;
using System.Linq;
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
        Assert.Equal("ja-JP", first.UiLocale);
        Assert.Equal("ja-JP", second.UiLocale);
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

    [Fact]
    public async Task GeneratesLocaleSpecificAdaptiveCards()
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

        var japanese = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "locale test",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UiLocale = "ja-JP"
        }, CancellationToken.None);

        var chinese = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "locale test",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UiLocale = "zh-CN"
        }, CancellationToken.None);

        Assert.Equal("ja-JP", japanese.UiLocale);
        Assert.Equal("zh-CN", chinese.UiLocale);

        var jaTitle = japanese.AdaptiveCard!["body"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var zhTitle = chinese.AdaptiveCard!["body"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        Assert.Equal("翻訳結果", jaTitle);
        Assert.Equal("翻译结果", zhTitle);
    }

    [Fact]
    public async Task ThrowsGlossaryConflictWhenDecisionMissing()
    {
        var options = Options.Create(new PluginOptions
        {
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

        var glossary = new GlossaryService();
        glossary.LoadEntries(new[]
        {
            new GlossaryEntry("GPU", "图形处理器", "tenant:contoso"),
            new GlossaryEntry("GPU", "显卡", "channel:finance")
        });

        var pipeline = BuildPipeline(options, glossary);

        await Assert.ThrowsAsync<GlossaryConflictException>(() => pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "GPU", 
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            ChannelId = "finance"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task AppliesGlossaryDecisionWhenProvided()
    {
        var options = Options.Create(new PluginOptions
        {
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

        var glossary = new GlossaryService();
        glossary.LoadEntries(new[]
        {
            new GlossaryEntry("GPU", "图形处理器", "tenant:contoso"),
            new GlossaryEntry("GPU", "显卡", "channel:finance")
        });

        var pipeline = BuildPipeline(options, glossary);

        var request = new TranslationRequest
        {
            Text = "GPU", 
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            ChannelId = "finance",
            GlossaryDecisions = new Dictionary<string, GlossaryDecision>(StringComparer.OrdinalIgnoreCase)
            {
                ["GPU"] = new GlossaryDecision
                {
                    Kind = GlossaryDecisionKind.UseAlternative,
                    Target = "显卡",
                    Scope = "channel:finance"
                }
            }
        };

        var result = await pipeline.ExecuteAsync(request, CancellationToken.None);

        var match = Assert.Single(result.GlossaryMatches);
        Assert.Equal(GlossaryDecisionKind.UseAlternative, match.Resolution);
        Assert.Equal("显卡", match.AppliedTarget);
    }

    private static TranslationPipeline BuildPipeline(IOptions<PluginOptions> options, GlossaryService? glossaryOverride = null)
    {
        var glossary = glossaryOverride ?? new GlossaryService();
        var localization = new LocalizationCatalogService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new TokenBroker(new KeyVaultSecretResolver(options), options), new UsageMetricsService(), localization, options);
        var cache = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        return new TranslationPipeline(router, glossary, new OfflineDraftStore(options), new LanguageDetector(), cache, throttle, options);
    }
}
