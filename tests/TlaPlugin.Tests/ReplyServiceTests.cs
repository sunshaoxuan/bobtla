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
        var service = new ReplyService(router, options);

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
        var service = new ReplyService(router, options);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "こんにちは",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { Tone = ToneTemplateService.Business }
        }, CancellationToken.None);

        Assert.Equal("sent", result.Status);
        Assert.Contains("商务语气", result.FinalText);
        Assert.Equal(ToneTemplateService.Business, result.ToneApplied);
    }

    private sealed class RecordingTokenBroker : ITokenBroker
    {
        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(10), "audience"));
        }
    }
}
