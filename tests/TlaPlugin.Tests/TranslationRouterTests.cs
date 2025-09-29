using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), options);
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

        Assert.Equal("backup", result.ModelId);
        Assert.Equal("ja", result.TargetLanguage);
        Assert.True(result.AdditionalTranslations.ContainsKey("fr"));
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

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), options);
        var request = new TranslationRequest
        {
            Text = new string('a', 200),
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        };

        await Assert.ThrowsAsync<BudgetExceededException>(() => router.TranslateAsync(request, CancellationToken.None));
    }
}
