using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
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
}
