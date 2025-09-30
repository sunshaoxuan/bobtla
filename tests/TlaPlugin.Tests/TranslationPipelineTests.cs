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

        Assert.False(first.RequiresLanguageSelection);
        Assert.False(second.RequiresLanguageSelection);

        var firstTranslation = Assert.NotNull(first.Translation);
        var secondTranslation = Assert.NotNull(second.Translation);

        Assert.Equal(firstTranslation.TranslatedText, secondTranslation.TranslatedText);
        Assert.Equal("ja-JP", firstTranslation.UiLocale);
        Assert.Equal("ja-JP", secondTranslation.UiLocale);
    }

    [Fact]
    public async Task UsesProvidedSourceLanguageWhenDetectionIsUncertain()
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

        var detection = await pipeline.DetectAsync(new LanguageDetectionRequest
        {
            Text = "12345",
            TenantId = "contoso"
        }, CancellationToken.None);

        Assert.True(detection.Confidence < 0.75);

        var result = await pipeline.TranslateAsync(new TranslationRequest
        {
            Text = "12345",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None);

        Assert.Equal("ja", result.TargetLanguage);
        Assert.Contains("12345", result.TranslatedText);
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

        var first = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "first",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None);

        Assert.NotNull(first.Translation);

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

        var japaneseResult = Assert.NotNull(japanese.Translation);
        var chineseResult = Assert.NotNull(chinese.Translation);

        Assert.Equal("ja-JP", japaneseResult.UiLocale);
        Assert.Equal("zh-CN", chineseResult.UiLocale);

        var jaTitle = japaneseResult.AdaptiveCard!["body"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var zhTitle = chineseResult.AdaptiveCard!["body"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        Assert.Equal("翻訳結果", jaTitle);
        Assert.Equal("翻译结果", zhTitle);
    }

    [Fact]
    public async Task RewriteAsyncRespectsEditedText()
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

        var pipeline = BuildPipeline(options);

        var rewrite = await pipeline.RewriteAsync(new RewriteRequest
        {
            Text = "Original",
            EditedText = "Custom content",
            TenantId = "contoso",
            UserId = "user",
            Tone = ToneTemplateService.Business
        }, CancellationToken.None);

        Assert.Contains("Custom content", rewrite.RewrittenText);
        Assert.Equal("primary", rewrite.ModelId);
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

        var translation = Assert.NotNull(result.Translation);
        var match = Assert.Single(translation.GlossaryMatches);
        Assert.Equal(GlossaryDecisionKind.UseAlternative, match.Resolution);
        Assert.Equal("显卡", match.AppliedTarget);
    }

    [Fact]
    public async Task ReturnsDetectionCandidatesWhenConfidenceLow()
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

        var pipeline = BuildPipeline(options);

        var result = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "Hello there",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja"
        }, CancellationToken.None);

        Assert.True(result.RequiresLanguageSelection);
        Assert.Null(result.Translation);
        var detection = Assert.NotNull(result.Detection);
        Assert.Equal("en", detection.Language);
        Assert.NotEmpty(detection.Candidates);
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "en", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReturnsLanguageSelectionForAmbiguousLatinSentence()
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

        var pipeline = BuildPipeline(options);

        var result = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "Besok pagi kami akan berangkat ke pasar untuk membeli sayur segar dan buah segar.",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja"
        }, CancellationToken.None);

        Assert.True(result.RequiresLanguageSelection);
        var detection = Assert.NotNull(result.Detection);
        Assert.True(detection.Confidence < 0.75);
    }

    [Fact]
    public async Task ReturnsJapaneseCandidateForKanjiOnlyDetection()
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

        var pipeline = BuildPipeline(options);

        var result = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "東京都庁",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "en"
        }, CancellationToken.None);

        Assert.True(result.RequiresLanguageSelection);
        var detection = Assert.NotNull(result.Detection);
        Assert.True(detection.Confidence < 0.75);
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReturnsJapaneseCandidateForKanjiOnlyDetectionWithSharedPunctuation()
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

        var pipeline = BuildPipeline(options);

        var result = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "東京都大阪府。",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "en"
        }, CancellationToken.None);

        Assert.True(result.RequiresLanguageSelection);
        var detection = Assert.NotNull(result.Detection);
        Assert.True(detection.Confidence < 0.75);
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "zh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RespectsManualSourceLanguageAfterDetection()
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

        var pipeline = BuildPipeline(options);
        var initialRequest = new TranslationRequest
        {
            Text = "Hello again",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja"
        };

        var detectionResult = await pipeline.ExecuteAsync(initialRequest, CancellationToken.None);
        var detected = Assert.NotNull(detectionResult.Detection);
        var selectedLanguage = detected.Candidates.First().Language;

        var manualRequest = new TranslationRequest
        {
            Text = initialRequest.Text,
            TenantId = initialRequest.TenantId,
            UserId = initialRequest.UserId,
            TargetLanguage = initialRequest.TargetLanguage,
            SourceLanguage = selectedLanguage
        };

        var translationResult = await pipeline.ExecuteAsync(manualRequest, CancellationToken.None);

        Assert.False(translationResult.RequiresLanguageSelection);
        var translation = Assert.NotNull(translationResult.Translation);
        Assert.Equal(selectedLanguage, translation.SourceLanguage);
        Assert.Equal("ja", translation.TargetLanguage);
    }

    [Fact]
    public async Task ReturnsBothHanCandidatesForSharedPunctuationDetection()
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

        var pipeline = BuildPipeline(options);

        var result = await pipeline.ExecuteAsync(new TranslationRequest
        {
            Text = "漢字表記のみ、句読点。",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "en"
        }, CancellationToken.None);

        Assert.True(result.RequiresLanguageSelection);
        var detection = Assert.NotNull(result.Detection);
        Assert.True(detection.Confidence < 0.75);
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(detection.Candidates, candidate => string.Equals(candidate.Language, "zh", StringComparison.OrdinalIgnoreCase));
    }

    private static TranslationPipeline BuildPipeline(IOptions<PluginOptions> options, GlossaryService? glossaryOverride = null)
    {
        var glossary = glossaryOverride ?? new GlossaryService();
        var localization = new LocalizationCatalogService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new TokenBroker(new KeyVaultSecretResolver(options), options), new UsageMetricsService(), localization, options);
        var cache = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var reply = new ReplyService(rewrite, options);
        return new TranslationPipeline(router, glossary, new OfflineDraftStore(options), new LanguageDetector(), cache, throttle, rewrite, reply, options);
    }
}
