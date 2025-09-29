using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using TlaPlugin.Teams;
using Xunit;

namespace TlaPlugin.Tests;

public class MessageExtensionHandlerTests
{
    [Fact]
    public async Task ReturnsAdaptiveCardResponse()
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

        var handler = BuildHandler(options);
        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        Assert.Equal("message", response["type"]?.GetValue<string>());
        var attachment = response["attachments"]!.AsArray().First().AsObject();
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment["contentType"]?.GetValue<string>());
    }

    [Fact]
    public async Task RendersAdditionalTranslationsInAdaptiveCard()
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

        var handler = BuildHandler(options);
        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "hello",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            AdditionalTargetLanguages = new List<string> { "fr" }
        });

        var card = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var texts = card["body"]!.AsArray().Select(node => node!.AsObject()["text"]?.GetValue<string>()).ToList();
        Assert.Contains("追加翻訳", texts);
        Assert.Contains(texts, text => text?.StartsWith("fr:") == true);
    }

    [Fact]
    public async Task ReturnsErrorCardWhenBudgetExceeded()
    {
        var options = Options.Create(new PluginOptions
        {
            DailyBudgetUsd = 0.00001m,
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

        var handler = BuildHandler(options);
        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = new string('a', 200),
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        Assert.Equal("message", response["type"]?.GetValue<string>());
        var body = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var title = body["body"]!.AsArray().First().AsObject();
        Assert.Contains("予算", title["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReturnsErrorCardWhenRateLimited()
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

        var handler = BuildHandler(options);

        await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "first",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "second",
            TenantId = "contoso",
            UserId = "user",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        var body = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var title = body["body"]!.AsArray().First().AsObject();
        Assert.Contains("レート", title["text"]!.GetValue<string>());
    }

    private static MessageExtensionHandler BuildHandler(IOptions<PluginOptions> options)
    {
        var glossary = new GlossaryService();
        glossary.LoadEntries(new[] { new GlossaryEntry("hello", "こんにちは", "tenant:contoso") });
        var compliance = new ComplianceGateway(options);
        var resolver = new KeyVaultSecretResolver(options);
        var router = new TranslationRouter(new ModelProviderFactory(options), compliance, new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), new TokenBroker(resolver, options), options);
        var cache = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        var pipeline = new TranslationPipeline(router, glossary, new OfflineDraftStore(options), new LanguageDetector(), cache, throttle, options);
        return new MessageExtensionHandler(pipeline);
    }
}
