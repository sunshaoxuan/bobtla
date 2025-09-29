using System;
using System.Collections.Generic;
using System.Security.Authentication;
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

        var tokenBroker = new RecordingTokenBroker();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), tokenBroker, options);
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

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), options);
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
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), broker, options);

        await Assert.ThrowsAsync<AuthenticationException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));
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

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), options);

        await Assert.ThrowsAsync<AuthenticationException>(() => router.TranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = string.Empty,
            TargetLanguage = "ja",
            SourceLanguage = "en"
        }, CancellationToken.None));
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
                throw new AuthenticationException("OBO フローの失敗");
            }

            return Task.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(5), "api://audience"));
        }
    }
}
