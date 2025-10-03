using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
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
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        Assert.Equal("message", response["type"]?.GetValue<string>());
        var attachment = response["attachments"]!.AsArray().First().AsObject();
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment["contentType"]?.GetValue<string>());
        var card = attachment["content"]!.AsObject();
        var body = card["body"]!.AsArray();
        var translated = body[1]!.AsObject()["text"]!.GetValue<string>();
        var actions = card["actions"]!.AsArray();
        var insertAction = actions.First()!.AsObject();
        Assert.Equal("Action.Submit", insertAction["type"]!.GetValue<string>());
        var teams = insertAction["msteams"]!.AsObject();
        Assert.Equal("messageBack", teams["type"]!.GetValue<string>());
        Assert.Equal(translated, teams["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReturnsLanguageSelectionWhenDetectionLow()
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
            Text = "Hello team",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "sso-token",
            TargetLanguage = "ja"
        });

        Assert.Equal("languageSelection", response["type"]?.GetValue<string>());
        var candidates = response["candidates"]!.AsArray();
        Assert.NotEmpty(candidates);
        var first = candidates[0]!.AsObject();
        Assert.True(first.ContainsKey("language"));
        Assert.True(first.ContainsKey("confidence"));
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
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            AdditionalTargetLanguages = new List<string> { "fr" }
        });

        var card = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var texts = card["body"]!.AsArray().Select(node => node!.AsObject()["text"]?.GetValue<string>()).ToList();
        Assert.Contains("追加の翻訳", texts);
        Assert.Contains(texts, text => text?.StartsWith("fr:") == true);
        var actions = card["actions"]!.AsArray();
        Assert.Contains(actions.Select(a => a!.AsObject()["data"]!.AsObject()["language"]!.GetValue<string>()), value => value == "fr");
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
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        Assert.Equal("message", response["type"]?.GetValue<string>());
        var body = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var title = body["body"]!.AsArray().First().AsObject();
        Assert.Contains("预算", title["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReturnsGlossaryConflictCardWhenResolutionRequired()
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

        var handler = BuildHandler(options, glossary);

        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "GPU",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            ChannelId = "finance"
        });

        Assert.Equal("glossaryConflict", response["type"]?.GetValue<string>());
        var attachment = response["attachments"]!.AsArray().First().AsObject();
        var card = attachment["content"]!.AsObject();
        Assert.Equal("AdaptiveCard", card["type"]!.GetValue<string>());
        var body = card["body"]!.AsArray();
        var choiceSet = body.First(node => node!.AsObject()["type"]!.GetValue<string>() == "Input.ChoiceSet")!.AsObject();
        var choices = choiceSet["choices"]!.AsArray();
        Assert.Equal(3, choices.Count);
        var encoded = choices[0]!.AsObject()["value"]!.GetValue<string>();
        var decoded = JsonNode.Parse(encoded)!.AsObject();
        Assert.Equal("GPU", decoded["source"]!.GetValue<string>());
        Assert.Equal("UsePreferred", decoded["kind"]!.GetValue<string>());
        var actions = card["actions"]!.AsArray();
        var submit = Assert.Single(actions
            .Select(action => action!.AsObject())
            .Where(node => node["data"]!.AsObject()["action"]!.GetValue<string>() == "resolveGlossary"));
        var submitData = submit["data"]!.AsObject();
        Assert.Equal("resolveGlossary", submitData["action"]!.GetValue<string>());
        var pendingRequest = submitData["pendingRequest"]!.AsObject();
        Assert.Equal("GPU", pendingRequest["text"]!.GetValue<string>());
        Assert.Equal("ja", pendingRequest["targetLanguage"]!.GetValue<string>());
        Assert.Equal("en", pendingRequest["sourceLanguage"]!.GetValue<string>());
        Assert.Equal("contoso", pendingRequest["tenantId"]!.GetValue<string>());
        Assert.True(pendingRequest["glossaryDecisions"]!.AsObject().Count == 0);
    }

    [Fact]
    public async Task GlossaryConflictCardIncludesAllCandidateChoices()
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
            new GlossaryEntry("GPU", "ユーザー訳", "user:user"),
            new GlossaryEntry("GPU", "チャンネル訳", "channel:finance"),
            new GlossaryEntry("GPU", "テナント訳", "tenant:contoso")
        });

        var handler = BuildHandler(options, glossary);

        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "GPU",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            ChannelId = "finance"
        });

        var attachment = response["attachments"]!.AsArray().First().AsObject();
        var card = attachment["content"]!.AsObject();
        var body = card["body"]!.AsArray();
        var choiceSet = body.First(node => node!.AsObject()["type"]!.GetValue<string>() == "Input.ChoiceSet")!.AsObject();
        var choices = choiceSet["choices"]!.AsArray();

        Assert.Equal(4, choices.Count);

        var decodedChoices = choices
            .Select(choice => JsonNode.Parse(choice!.AsObject()["value"]!.GetValue<string>())!.AsObject())
            .ToList();

        var preferred = Assert.Single(decodedChoices.Where(node => node["kind"]!.GetValue<string>() == nameof(GlossaryDecisionKind.UsePreferred)));
        Assert.Equal("ユーザー訳", preferred["target"]!.GetValue<string>());
        Assert.Equal("user:user", preferred["scope"]!.GetValue<string>());

        var alternatives = decodedChoices
            .Where(node => node["kind"]!.GetValue<string>() == nameof(GlossaryDecisionKind.UseAlternative))
            .ToList();
        Assert.Equal(2, alternatives.Count);
        Assert.Contains(alternatives, node => node["target"]!.GetValue<string>() == "チャンネル訳" && node["scope"]!.GetValue<string>() == "channel:finance");
        Assert.Contains(alternatives, node => node["target"]!.GetValue<string>() == "テナント訳" && node["scope"]!.GetValue<string>() == "tenant:contoso");

        var keepOriginal = Assert.Single(decodedChoices.Where(node => node["kind"]!.GetValue<string>() == nameof(GlossaryDecisionKind.KeepOriginal)));
        Assert.False(keepOriginal.ContainsKey("target"));
    }

    [Fact]
    public async Task ReturnsLocalizedErrorCardWhenLocaleProvided()
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
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UiLocale = "zh-CN"
        });

        var body = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var title = body["body"]!.AsArray().First().AsObject();
        Assert.Contains("预算", title["text"]!.GetValue<string>());
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
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        var response = await handler.HandleTranslateAsync(new TranslationRequest
        {
            Text = "second",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "sso-token",
            TargetLanguage = "ja",
            SourceLanguage = "en"
        });

        var body = response["attachments"]!.AsArray().First()["content"]!.AsObject();
        var title = body["body"]!.AsArray().First().AsObject();
        Assert.Contains("速率", title["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task HandleReplyAsyncReturnsLanguageSelectionWhenDetectionLow()
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
        var response = await handler.HandleReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            Text = "12345",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            UserAssertion = "sso-token",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        });

        Assert.Equal("languageSelection", response["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleReplyAsyncUsesEditedTextDuringRewrite()
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
        var response = await handler.HandleReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            Text = "Team update",
            EditedText = "自定义编辑内容",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            UserAssertion = "sso-token",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        });

        Assert.Equal("replyPosted", response["type"]?.GetValue<string>());
        Assert.Contains("自定义编辑内容", response["finalText"]!.GetValue<string>());
        Assert.Equal(ToneTemplateService.Business, response["toneApplied"]?.GetValue<string>());
        Assert.Equal("reply-001", response["messageId"]?.GetValue<string>());
        Assert.Equal("sent", response["status"]?.GetValue<string>());
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O"), response["postedAt"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleReplyAsyncReusesUserAssertionWhenPostingReply()
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

        var tokenBroker = new CapturingTokenBroker();
        var handler = BuildHandler(options, tokenBrokerOverride: tokenBroker);
        var response = await handler.HandleReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            Text = "Team update",
            EditedText = "调整后的内容",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            UserAssertion = "sso-token",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        });

        Assert.Equal("replyPosted", response["type"]?.GetValue<string>());
        Assert.Equal("sso-token", tokenBroker.LastUserAssertion);
        Assert.Equal("contoso", tokenBroker.LastTenantId);
        Assert.Equal("user", tokenBroker.LastUserId);
    }

    [Fact]
    public async Task HandleReplyAsyncReturnsErrorCardWhenUnauthorized()
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
            },
            Security = new SecurityOptions
            {
                AllowedReplyChannels = new List<string> { "allowed" }
            }
        });

        var handler = BuildHandler(options);
        var response = await handler.HandleReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            Text = "Hello team",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "blocked",
            UserAssertion = "sso-token",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        });

        Assert.Equal("message", response["type"]?.GetValue<string>());
        var attachment = response["attachments"]!.AsArray().First().AsObject();
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment["contentType"]!.GetValue<string>());
    }

    private static MessageExtensionHandler BuildHandler(IOptions<PluginOptions> options, GlossaryService? glossaryOverride = null, ContextRetrievalService? contextOverride = null, ITeamsReplyClient? replyClientOverride = null, ITokenBroker? tokenBrokerOverride = null)
    {
        var glossary = glossaryOverride ?? new GlossaryService();
        if (glossaryOverride is null)
        {
            glossary.LoadEntries(new[] { new GlossaryEntry("hello", "你好", "tenant:contoso") });
        }
        var compliance = new ComplianceGateway(options);
        var resolver = new KeyVaultSecretResolver(options);
        var localization = new LocalizationCatalogService();
        var metrics = new UsageMetricsService();
        var tokenBroker = tokenBrokerOverride ?? new TokenBroker(resolver, options);
        var router = new TranslationRouter(new ModelProviderFactory(options), compliance, new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), tokenBroker, metrics, localization, options);
        var cache = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var replyClient = replyClientOverride ?? new StubTeamsReplyClient();
        var reply = new ReplyService(rewrite, router, replyClient, tokenBroker, metrics, options);
        var context = contextOverride ?? new ContextRetrievalService(new NullTeamsMessageClient(), new MemoryCache(new MemoryCacheOptions()), tokenBroker, options);
        var pipeline = new TranslationPipeline(router, glossary, new OfflineDraftStore(options), new LanguageDetector(), cache, throttle, context, rewrite, reply, options);
        return new MessageExtensionHandler(pipeline, localization, options);
    }

    private sealed class CapturingTokenBroker : ITokenBroker
    {
        public string? LastTenantId { get; private set; }
        public string? LastUserId { get; private set; }
        public string? LastUserAssertion { get; private set; }

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, string? userAssertion, CancellationToken cancellationToken)
        {
            LastTenantId = tenantId;
            LastUserId = userId;
            LastUserAssertion = userAssertion;
            return Task.FromResult(new AccessToken("obo-token", DateTimeOffset.UtcNow.AddMinutes(5), "api://tla-plugin"));
        }
    }

    private sealed class StubTeamsReplyClient : ITeamsReplyClient
    {
        public Task<TeamsReplyResponse> SendReplyAsync(TeamsReplyRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new TeamsReplyResponse("reply-001", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), "sent"));
    }
}
