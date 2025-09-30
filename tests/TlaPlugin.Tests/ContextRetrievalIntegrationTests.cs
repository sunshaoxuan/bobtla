using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ContextRetrievalIntegrationTests
{
    [Fact]
    public async Task RetrievesContextThroughGraphClient()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var graph = new FakeTeamsMessageClient();
        graph.Messages.AddRange(new[]
        {
            new ContextMessage { Author = "Alex", Text = "Budget approval pending", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3) },
            new ContextMessage { Author = "Taylor", Text = "Contract draft ready for review", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
        });

        var pipeline = BuildPipeline(options, graph, cache);

        var result = await pipeline.TranslateAsync(new TranslationRequest
        {
            Text = "Here is the purchase summary",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UseRag = true
        }, CancellationToken.None);

        var translation = Assert.NotNull(result.Translation);
        Assert.Contains("[Context]", translation.RawTranslatedText, StringComparison.Ordinal);
        Assert.Equal(1, graph.CallCount);
    }

    [Fact]
    public async Task AppliesHintFilteringWhenProvided()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var graph = new FakeTeamsMessageClient();
        graph.Messages.AddRange(new[]
        {
            new ContextMessage { Author = "Alex", Text = "Budget approval pending", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3) },
            new ContextMessage { Author = "Taylor", Text = "Contract draft ready for review", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
        });

        var pipeline = BuildPipeline(options, graph, cache);

        var result = await pipeline.TranslateAsync(new TranslationRequest
        {
            Text = "Here is the purchase summary",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UseRag = true,
            ContextHints = new List<string> { "contract" }
        }, CancellationToken.None);

        var translation = Assert.NotNull(result.Translation);
        Assert.Contains("Contract draft", translation.RawTranslatedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Budget approval", translation.RawTranslatedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsesCachedMessagesOnSubsequentRequests()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var graph = new FakeTeamsMessageClient();
        graph.Messages.AddRange(new[]
        {
            new ContextMessage { Author = "Alex", Text = "Budget approval pending", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-3) },
            new ContextMessage { Author = "Taylor", Text = "Contract draft ready for review", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1) }
        });

        var pipeline = BuildPipeline(options, graph, cache);

        var request = new TranslationRequest
        {
            Text = "Here is the purchase summary",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UseRag = true
        };

        var first = await pipeline.TranslateAsync(request, CancellationToken.None);
        var second = await pipeline.TranslateAsync(request, CancellationToken.None);

        Assert.NotNull(first.Translation);
        Assert.NotNull(second.Translation);
        Assert.Equal(1, graph.CallCount);
    }

    [Fact]
    public async Task FallsBackWhenGraphThrows()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var graph = new FakeTeamsMessageClient { ShouldThrow = true };

        var pipeline = BuildPipeline(options, graph, cache);

        var result = await pipeline.TranslateAsync(new TranslationRequest
        {
            Text = "Here is the purchase summary",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            TargetLanguage = "ja",
            SourceLanguage = "en",
            UseRag = true
        }, CancellationToken.None);

        var translation = Assert.NotNull(result.Translation);
        Assert.DoesNotContain("[Context]", translation.RawTranslatedText, StringComparison.Ordinal);
        Assert.Equal(1, graph.CallCount);
    }

    private static TranslationPipeline BuildPipeline(IOptions<PluginOptions> options, ITeamsMessageClient graph, IMemoryCache cache)
    {
        var glossary = new GlossaryService();
        var localization = new LocalizationCatalogService();
        var router = new TranslationRouter(
            new ModelProviderFactory(options),
            new ComplianceGateway(options),
            new BudgetGuard(options.Value),
            new AuditLogger(),
            new ToneTemplateService(),
            new TokenBroker(new KeyVaultSecretResolver(options), options),
            new UsageMetricsService(),
            localization,
            options);

        var context = new ContextRetrievalService(graph, cache, options);
        var cacheStore = new TranslationCache(options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        var reply = new ReplyService(rewrite, options);

        return new TranslationPipeline(
            router,
            glossary,
            new OfflineDraftStore(options),
            new LanguageDetector(),
            cacheStore,
            throttle,
            context,
            rewrite,
            reply,
            options);
    }

    private static IOptions<PluginOptions> CreateOptions()
    {
        return Options.Create(new PluginOptions
        {
            DailyBudgetUsd = 1m,
            CacheTtl = TimeSpan.FromHours(1),
            RequestsPerMinute = 10,
            MaxConcurrentTranslations = 2,
            Rag = new RagOptions
            {
                Enabled = true,
                MaxMessages = 5,
                SummaryThreshold = 1200,
                SummaryTargetLength = 480
            },
            Providers = new List<ModelProviderOptions>
            {
                new()
                {
                    Id = "primary",
                    CostPerCharUsd = 0.1m,
                    Regions = new List<string> { "japan" },
                    Certifications = new List<string> { "iso" }
                }
            },
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso" }
            }
        });
    }

    private sealed class FakeTeamsMessageClient : ITeamsMessageClient
    {
        public List<ContextMessage> Messages { get; } = new();
        public int CallCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
            string tenantId,
            string? channelId,
            string? threadId,
            int maxMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Graph failure");
            }

            var ordered = Messages
                .OrderByDescending(message => message.Timestamp)
                .Take(Math.Max(1, maxMessages))
                .Select(message => new ContextMessage
                {
                    Id = message.Id,
                    Author = message.Author,
                    Timestamp = message.Timestamp,
                    Text = message.Text,
                    RelevanceScore = message.RelevanceScore
                })
                .ToList();

            IReadOnlyList<ContextMessage> snapshot = ordered;
            return Task.FromResult(snapshot);
        }
    }
}
