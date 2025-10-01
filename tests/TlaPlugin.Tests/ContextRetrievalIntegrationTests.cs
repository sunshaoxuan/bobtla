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

    [Fact]
    public async Task RetrievesMessagesWithValidToken()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var token = new AccessToken("valid-token", DateTimeOffset.UtcNow.AddMinutes(5), "api://audience");
        var broker = new RecordingTokenBroker(token);
        var graph = new TokenAwareTeamsMessageClient();
        graph.Messages.Add(new ContextMessage
        {
            Author = "Alex",
            Text = "Budget approval pending",
            Timestamp = DateTimeOffset.UtcNow
        });

        var service = new ContextRetrievalService(graph, cache, broker, options);

        var result = await service.GetContextAsync(new ContextRetrievalRequest
        {
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            MaxMessages = 3
        }, CancellationToken.None);

        Assert.True(result.HasContext);
        Assert.Equal("valid-token", graph.LastAccessToken?.Value);
        Assert.Equal("user", graph.LastUserId);
        Assert.Equal(1, broker.CallCount);
    }

    [Fact]
    public async Task ReturnsEmptyWhenTokenExpired()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var broker = new RecordingTokenBroker(new AccessToken("expired", DateTimeOffset.UtcNow.AddMinutes(-5), "aud"));
        var graph = new TokenAwareTeamsMessageClient { TreatExpiredAsUnauthorized = true };

        var service = new ContextRetrievalService(graph, cache, broker, options);

        var result = await service.GetContextAsync(new ContextRetrievalRequest
        {
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            MaxMessages = 3
        }, CancellationToken.None);

        Assert.False(result.HasContext);
        Assert.Equal(1, graph.UnauthorizedCount);
        Assert.Equal(1, broker.CallCount);
    }

    [Fact]
    public async Task ReturnsEmptyWhenAccessDenied()
    {
        var options = CreateOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var broker = new RecordingTokenBroker(new AccessToken("valid", DateTimeOffset.UtcNow.AddMinutes(5), "aud"));
        var graph = new TokenAwareTeamsMessageClient { SimulateForbidden = true };

        var service = new ContextRetrievalService(graph, cache, broker, options);

        var result = await service.GetContextAsync(new ContextRetrievalRequest
        {
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "general",
            MaxMessages = 3
        }, CancellationToken.None);

        Assert.False(result.HasContext);
        Assert.Equal(1, graph.ForbiddenCount);
        Assert.Equal(1, broker.CallCount);
    }

    private static TranslationPipeline BuildPipeline(IOptions<PluginOptions> options, ITeamsMessageClient graph, IMemoryCache cache)
    {
        var glossary = new GlossaryService();
        var localization = new LocalizationCatalogService();
        var tokenBroker = new TokenBroker(new KeyVaultSecretResolver(options), options);
        var router = new TranslationRouter(
            new ModelProviderFactory(options),
            new ComplianceGateway(options),
            new BudgetGuard(options.Value),
            new AuditLogger(),
            new ToneTemplateService(),
            tokenBroker,
            new UsageMetricsService(),
            localization,
            options);

        var context = new ContextRetrievalService(graph, cache, tokenBroker, options);
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
        public AccessToken? LastToken { get; private set; }
        public string? LastUserId { get; private set; }

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

        public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
            string tenantId,
            string? channelId,
            string? threadId,
            int maxMessages,
            AccessToken? accessToken,
            string? userId,
            CancellationToken cancellationToken)
        {
            LastToken = accessToken;
            LastUserId = userId;
            return GetRecentMessagesAsync(tenantId, channelId, threadId, maxMessages, cancellationToken);
        }
    }

    private sealed class TokenAwareTeamsMessageClient : ITeamsMessageClient
    {
        public List<ContextMessage> Messages { get; } = new();
        public AccessToken? LastAccessToken { get; private set; }
        public string? LastUserId { get; private set; }
        public bool TreatExpiredAsUnauthorized { get; set; }
        public bool SimulateForbidden { get; set; }
        public int UnauthorizedCount { get; private set; }
        public int ForbiddenCount { get; private set; }

        public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
            string tenantId,
            string? channelId,
            string? threadId,
            int maxMessages,
            CancellationToken cancellationToken)
        {
            return GetRecentMessagesAsync(tenantId, channelId, threadId, maxMessages, null, null, cancellationToken);
        }

        public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
            string tenantId,
            string? channelId,
            string? threadId,
            int maxMessages,
            AccessToken? accessToken,
            string? userId,
            CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            LastUserId = userId;

            if (TreatExpiredAsUnauthorized && accessToken is not null && accessToken.ExpiresOn <= DateTimeOffset.UtcNow)
            {
                UnauthorizedCount++;
                return Task.FromResult<IReadOnlyList<ContextMessage>>(Array.Empty<ContextMessage>());
            }

            if (SimulateForbidden)
            {
                ForbiddenCount++;
                return Task.FromResult<IReadOnlyList<ContextMessage>>(Array.Empty<ContextMessage>());
            }

            IReadOnlyList<ContextMessage> snapshot = Messages
                .OrderByDescending(message => message.Timestamp)
                .Take(Math.Max(1, maxMessages))
                .ToList();

            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingTokenBroker : ITokenBroker
    {
        private readonly List<AccessToken> _tokens;
        private int _index;

        public RecordingTokenBroker(params AccessToken[] tokens)
        {
            if (tokens is null || tokens.Length == 0)
            {
                throw new ArgumentException("At least one token must be provided.", nameof(tokens));
            }

            _tokens = new List<AccessToken>(tokens);
        }

        public int CallCount { get; private set; }

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            CallCount++;
            var token = _tokens[Math.Min(_index, _tokens.Count - 1)];
            if (_index < _tokens.Count - 1)
            {
                _index++;
            }

            return Task.FromResult(token);
        }
    }
}
