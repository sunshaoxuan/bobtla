using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ReplyServiceTests
{
    [Fact]
    public async Task ThrowsWhenChannelNotAllowed()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                AllowedReplyChannels = new List<string> { "general" }
            },
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var service = new ReplyService(rewrite, options);

        await Assert.ThrowsAsync<ReplyAuthorizationException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "hello",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "random"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task AppliesToneWhenLanguagePolicySpecified()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                AllowedReplyChannels = new List<string>()
            },
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var service = new ReplyService(rewrite, options);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "こんにちは",
            EditedText = "手动调整",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { Tone = ToneTemplateService.Business, TargetLang = "ja" }
        }, CancellationToken.None);

        Assert.Equal("sent", result.Status);
        Assert.Contains("手动调整", result.FinalText);
        Assert.Contains("商务语气", result.FinalText);
        Assert.Equal(ToneTemplateService.Business, result.ToneApplied);
        Assert.Equal("ja", result.Language);
    }

    [Fact]
    public async Task ThrowsWhenThreadIdMissing()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var service = new ReplyService(rewrite, options);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ReplyText = "hi",
            TenantId = "contoso",
            UserId = "user"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UsesFinalTextOverrideWhenProvided()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var service = new ReplyService(rewrite, options);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "ignored",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        }, "最终文本", ToneTemplateService.Business, CancellationToken.None);

        Assert.Equal("最终文本", result.FinalText);
        Assert.Equal(ToneTemplateService.Business, result.ToneApplied);
    }

    [Fact]
    public async Task ThrowsWhenFinalTextOverrideMissing()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new RecordingTokenBroker(), new UsageMetricsService(), new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var service = new ReplyService(rewrite, options);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "ignored",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja" }
        }, string.Empty, null, CancellationToken.None));
    }

    private sealed class RecordingTokenBroker : ITokenBroker
    {
        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(10), "audience"));
        }
    }
}
