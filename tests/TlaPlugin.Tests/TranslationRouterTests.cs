using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Providers;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class TranslationRouterTests
{
    [Fact]
    public async Task UsesFallbackProviderWhenPrimaryFails()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", SimulatedFailures = 2, CostPerCharUsd = 0.00002m, Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"} },
                new() { Id = "backup", SimulatedFailures = 0, CostPerCharUsd = 0.00002m, Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"}, TranslationPrefix = "[Backup]" }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var tokenBroker = new RecordingTokenBroker();
        var metrics = new UsageMetricsService();
        var localization = new LocalizationCatalogService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), tokenBroker, metrics, localization, options);
        var request = new TranslationRequest
        {
            Text = "hello world",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            AdditionalTargetLanguages = new List<string> { "fr" }
        };

        var result = await router.TranslateAsync(request, CancellationToken.None);

        Assert.Equal(1, tokenBroker.Calls);
        Assert.Equal("backup", result.ModelId);
        Assert.Equal("ja", result.TargetLanguage);
        Assert.True(result.AdditionalTranslations.ContainsKey("fr"));
        Assert.EndsWith("※敬体に調整済み", result.AdditionalTranslations["fr"]);
        var expectedCost = request.Text.Length * 0.00002m * 2;
        Assert.Equal(expectedCost, result.CostUsd);
    }

    [Fact]
    public async Task ThrowsWhenBudgetExceeded()
    {
        var options = Options.Create(new PluginOptions
        {
            DailyBudgetUsd = 0.00001m,
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", SimulatedFailures = 0, CostPerCharUsd = 0.1m, Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), metrics, new LocalizationCatalogService(), options);
        var request = new TranslationRequest
        {
            Text = new string('a', 200),
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        };

        await Assert.ThrowsAsync<BudgetExceededException>(() => router.TranslateAsync(request, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        var failure = Assert.Single(tenant.Failures);
        Assert.Equal(UsageMetricsService.FailureReasons.Budget, failure.Reason);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public async Task ThrowsWhenTokenBrokerFails()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var broker = new RecordingTokenBroker { ShouldThrow = true };
        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), broker, metrics, new LocalizationCatalogService(), options);

        await Assert.ThrowsAsync<AuthenticationException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        var failure = Assert.Single(tenant.Failures);
        Assert.Equal(UsageMetricsService.FailureReasons.Authentication, failure.Reason);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public async Task RequiresUserIdForTokenExchange()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), metrics, new LocalizationCatalogService(), options);

        await Assert.ThrowsAsync<AuthenticationException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = string.Empty,
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        var failure = Assert.Single(tenant.Failures);
        Assert.Equal(UsageMetricsService.FailureReasons.Authentication, failure.Reason);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public async Task AppendsAdditionalTranslationsToCardAndAudit()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", Regions = new List<string>{"japan"}, Certifications = new List<string>{"iso27001"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var audit = new AuditLogger();
        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), audit, new ToneTemplateService(), new RecordingTokenBroker(), metrics, new LocalizationCatalogService(), options);
        var request = new TranslationRequest
        {
            Text = "hello world",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            AdditionalTargetLanguages = new List<string> { "fr", "de" }
        };

        var result = await router.TranslateAsync(request, CancellationToken.None);

        var body = result.AdaptiveCard["body"]!.AsArray().Select(node => node!.AsObject()).ToList();
        Assert.Contains(body, block => block["text"]?.GetValue<string>() == "追加の翻訳");
        Assert.Contains(body, block => block["text"]?.GetValue<string>()?.StartsWith("fr:") == true);
        Assert.Contains(body, block => block["text"]?.GetValue<string>()?.StartsWith("de:") == true);
        var actions = result.AdaptiveCard["actions"]!.AsArray();
        Assert.Contains(actions.Select(a => a!.AsObject()["data"]!.AsObject()["language"]!.GetValue<string>()), language => language == "fr");
        Assert.Contains(actions.Select(a => a!.AsObject()["data"]!.AsObject()["language"]!.GetValue<string>()), language => language == "de");

        var log = audit.Export().Single();
        var extras = log["additionalTranslations"]!.AsObject();
        Assert.Equal(2, extras.Count);
        Assert.True(extras.ContainsKey("fr"));
        Assert.True(extras.ContainsKey("de"));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants, snapshot => snapshot.TenantId == "contoso");
        Assert.Equal(3, tenant.Translations);
        Assert.Equal(result.CostUsd, tenant.TotalCostUsd);
        Assert.Contains(tenant.Models, model => model.ModelId == result.ModelId && model.Translations == 3);
        Assert.Empty(tenant.Failures);
    }

    [Fact]
    public async Task RecordsMetricsAcrossTenants()
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

        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), metrics, new LocalizationCatalogService(), options);

        var first = await router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            AdditionalTargetLanguages = new List<string> { "fr" }
        }, CancellationToken.None);

        var second = await router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "fabrikam",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None);

        var report = metrics.GetReport();
        Assert.Equal(2, report.Tenants.Count);
        var overall = report.Overall;
        Assert.Equal(3, overall.Translations);
        Assert.Equal(first.CostUsd + second.CostUsd, overall.TotalCostUsd);
        Assert.True(overall.AverageLatencyMs >= 0);
        Assert.Empty(overall.Failures);
    }

    [Fact]
    public async Task RecordsComplianceFailureWhenAllProvidersBlocked()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary", Regions = new List<string>{"us"}, Certifications = new List<string>{"iso"} }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso" }
            }
        });

        var metrics = new UsageMetricsService();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), metrics, new LocalizationCatalogService(), options);

        await Assert.ThrowsAsync<TranslationException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        var failure = Assert.Single(tenant.Failures);
        Assert.Equal(UsageMetricsService.FailureReasons.Compliance, failure.Reason);
        Assert.Equal(1, failure.Count);
    }

    [Fact]
    public async Task BuildsAdaptiveCardWithRequestedLocale()
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

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);

        var result = await router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UiLocale = "zh-CN"
        }, CancellationToken.None);

        Assert.Equal("zh-CN", result.UiLocale);
        var title = result.AdaptiveCard!["body"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        Assert.Equal("翻译结果", title);
    }

    [Fact]
    public async Task DetectAsyncReturnsResult()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary" }
            }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);

        var result = await router.DetectAsync(new LanguageDetectionRequest { Text = "こんにちは", TenantId = "contoso" }, CancellationToken.None);

        Assert.Equal("ja", result.Language);
        Assert.True(result.Confidence > 0.5);
    }

    [Fact]
    public async Task RewriteAsyncAdjustsTone()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary" }
            }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);

        var result = await router.RewriteAsync(new RewriteRequest
        {
            Text = "こんにちは",
            TenantId = "contoso",
            UserId = "user",
            Tone = ToneTemplateService.Business
        }, CancellationToken.None);

        Assert.Equal("primary", result.ModelId);
        Assert.Contains("商务语气", result.RewrittenText);
    }

    [Fact]
    public async Task SummarizeAsyncProducesSummary()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions>
            {
                new() { Id = "primary" }
            }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);

        var summary = await router.SummarizeAsync(new SummarizeRequest
        {
            Context = "This is a very long context that should be summarized for verification.",
            TenantId = "contoso",
            UserId = "user"
        }, CancellationToken.None);

        Assert.Equal("primary", summary.ModelId);
        Assert.Contains("概要", summary.Summary);
    }

    [Fact]
    public async Task TranslateAsyncRequestsLanguageSelectionWhenConfidenceLow()
    {
        var providerOptions = new ModelProviderOptions
        {
            Id = "stub",
            Regions = new List<string> { "japan" },
            Certifications = new List<string> { "iso27001" }
        };

        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { providerOptions },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso27001" }
            }
        });

        var detection = new DetectionResult(
            "xx",
            0.42,
            new List<DetectionCandidate>
            {
                new("xx", 0.42),
                new("yy", 0.38)
            });

        var provider = new StubModelProvider(providerOptions, detection);
        var router = new TranslationRouter(
            new ModelProviderFactory(options),
            new ComplianceGateway(options),
            new BudgetGuard(options.Value),
            new AuditLogger(),
            new ToneTemplateService(),
            new RecordingTokenBroker(),
            new UsageMetricsService(),
            new LocalizationCatalogService(),
            options,
            new[] { provider });

        var ex = await Assert.ThrowsAsync<LowConfidenceDetectionException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "12345",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja"
        }, CancellationToken.None));

        Assert.Equal(detection.Language, ex.Detection.Language);
        Assert.True(ex.Detection.Confidence < 0.75);
        Assert.Contains(ex.Detection.Candidates, candidate => candidate.Language == "xx");
    }

    private sealed class RecordingTokenBroker : ITokenBroker
    {
        public bool ShouldThrow { get; set; }
        public int Calls { get; private set; }

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            Calls++;
            if (ShouldThrow)
            {
                throw new AuthenticationException("OBO フローが失敗しました");
            }

            return Task.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(5), "api://audience"));
        }
    }

    private sealed class StubModelProvider : IModelProvider
    {
        private readonly DetectionResult _detection;

        public StubModelProvider(ModelProviderOptions options, DetectionResult detection)
        {
            Options = options;
            _detection = detection;
        }

        public ModelProviderOptions Options { get; }

        public Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(_detection);

        public Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Translation should not be invoked when detection confidence is low.");

        public Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken)
            => Task.FromResult(translatedText);

        public Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(string.Empty);
    }
}
