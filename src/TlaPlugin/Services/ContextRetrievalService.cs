using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// Teams チャネルから最近のメッセージを取得し、RAG 用コンテキストを提供するサービス。
/// </summary>
public class ContextRetrievalService
{
    private readonly ITeamsMessageClient _teamsClient;
    private readonly IMemoryCache _cache;
    private readonly ITokenBroker _tokenBroker;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
    private readonly PluginOptions _options;
    private readonly ILogger<ContextRetrievalService>? _logger;

    public ContextRetrievalService(
        ITeamsMessageClient teamsClient,
        IMemoryCache cache,
        ITokenBroker tokenBroker,
        IOptions<PluginOptions>? options = null,
        ILogger<ContextRetrievalService>? logger = null)
    {
        _teamsClient = teamsClient ?? throw new ArgumentNullException(nameof(teamsClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tokenBroker = tokenBroker ?? throw new ArgumentNullException(nameof(tokenBroker));
        _options = options?.Value ?? new PluginOptions();
        _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<ContextRetrievalResult> GetContextAsync(ContextRetrievalRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            _logger?.LogWarning("ContextRetrievalService skipping request due to missing tenant or user. Tenant: {TenantId}. User: {UserId}.", request.TenantId, request.UserId);
            return new ContextRetrievalResult();
        }

        var cacheKey = BuildChannelKey(request.TenantId, request.ThreadId ?? request.ChannelId ?? string.Empty);
        var hints = request.ContextHints ?? new List<string>();
        _logger?.LogDebug(
            "ContextRetrievalService resolving context for tenant {TenantId}, key {CacheKey}. HintCount={HintCount}, MaxMessages={MaxMessages}.",
            request.TenantId,
            cacheKey,
            hints.Count,
            request.MaxMessages);
        var candidates = await GetOrFetchMessagesAsync(cacheKey, request, cancellationToken).ConfigureAwait(false);

        var messages = ApplyFilters(candidates, hints, request.MaxMessages);

        _logger?.LogInformation(
            "ContextRetrievalService returning {MessageCount} messages for tenant {TenantId} (cache key {CacheKey}).",
            messages.Count,
            request.TenantId,
            cacheKey);

        return new ContextRetrievalResult
        {
            Messages = messages
        };
    }

    public void SeedMessages(string tenantId, string? channelId, IEnumerable<ContextMessage> messages)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        var key = BuildChannelKey(tenantId, channelId ?? string.Empty);
        var snapshot = messages
            .Select(Clone)
            .OrderByDescending(message => message.Timestamp)
            .ToList()
            .AsReadOnly();

        _cache.Set(key, snapshot, BuildCacheEntryOptions());
    }

    private static string BuildChannelKey(string tenantId, string channelId)
    {
        return $"{tenantId}:{channelId}";
    }

    private async Task<IReadOnlyList<ContextMessage>> GetOrFetchMessagesAsync(
        string cacheKey,
        ContextRetrievalRequest request,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<ContextMessage> cached))
        {
            _logger?.LogDebug("ContextRetrievalService cache hit for {CacheKey}. Returning {Count} messages.", cacheKey, cached.Count);
            return cached;
        }

        var refreshLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
            {
                _logger?.LogDebug("ContextRetrievalService cache populated for {CacheKey} while waiting. Returning {Count} messages.", cacheKey, cached.Count);
                return cached;
            }

            var limit = request.MaxMessages ?? _options.Rag.MaxMessages;
            if (limit <= 0)
            {
                limit = _options.Rag.MaxMessages > 0 ? _options.Rag.MaxMessages : 10;
            }

            if (string.IsNullOrWhiteSpace(request.UserAssertion))
            {
                _logger?.LogWarning("ContextRetrievalService missing user assertion for tenant {TenantId}, cache key {CacheKey}. Returning empty result.", request.TenantId, cacheKey);
                return Array.Empty<ContextMessage>();
            }

            AccessToken? accessToken = null;
            try
            {
                var stopwatch = Stopwatch.StartNew();
                accessToken = await _tokenBroker
                    .ExchangeOnBehalfOfAsync(request.TenantId, request.UserId, request.UserAssertion, cancellationToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();
                _logger?.LogInformation(
                    "ContextRetrievalService acquired OBO token for tenant {TenantId} in {ElapsedMs} ms.",
                    request.TenantId,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (AuthenticationException)
            {
                _logger?.LogWarning("ContextRetrievalService failed to exchange token for tenant {TenantId}, cache key {CacheKey}.", request.TenantId, cacheKey);
                return Array.Empty<ContextMessage>();
            }

            var fetchWatch = Stopwatch.StartNew();
            var fetched = await _teamsClient
                .GetRecentMessagesAsync(
                    request.TenantId,
                    request.ChannelId,
                    request.ThreadId,
                    limit,
                    accessToken,
                    request.UserId,
                    request.UserAssertion,
                    cancellationToken)
                .ConfigureAwait(false);
            fetchWatch.Stop();
            _logger?.LogInformation(
                "ContextRetrievalService fetched {MessageCount} messages for tenant {TenantId} in {ElapsedMs} ms (cache key {CacheKey}).",
                fetched.Count,
                request.TenantId,
                fetchWatch.ElapsedMilliseconds,
                cacheKey);

            var snapshot = fetched
                .Select(Clone)
                .OrderByDescending(message => message.Timestamp)
                .ToList()
                .AsReadOnly();

            _cache.Set(cacheKey, snapshot, BuildCacheEntryOptions());
            _logger?.LogDebug("ContextRetrievalService cached {Count} messages for {CacheKey} with expiration {Expiration}.", snapshot.Count, cacheKey, _options.Rag.CacheExpiration);
            return snapshot;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private IReadOnlyList<ContextMessage> ApplyFilters(
        IReadOnlyList<ContextMessage> candidates,
        IList<string> hints,
        int? overrideLimit)
    {
        IEnumerable<ContextMessage> filtered = candidates.OrderByDescending(message => message.Timestamp);

        if (hints.Count > 0)
        {
            var hinted = filtered
                .Where(message => hints.Any(hint =>
                    message.Text.Contains(hint, StringComparison.OrdinalIgnoreCase)
                    || message.Author.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            _logger?.LogDebug("ContextRetrievalService applied {HintCount} hints and reduced message set to {Count} candidates.", hints.Count, hinted.Count);
            filtered = hinted;
        }

        var limit = overrideLimit ?? _options.Rag.MaxMessages;
        List<ContextMessage> limited;
        if (limit > 0)
        {
            limited = filtered.Take(limit).ToList();
            _logger?.LogTrace("ContextRetrievalService applied max message limit {Limit}.", limit);
        }
        else
        {
            limited = filtered as List<ContextMessage> ?? filtered.ToList();
        }

        var ordered = limited
            .Select(Clone)
            .ToList();

        if (_options.Rag.SummaryThreshold > 0)
        {
            ordered = TrimByThreshold(ordered, _options.Rag.SummaryThreshold);
            _logger?.LogTrace("ContextRetrievalService trimmed messages to summary threshold {Threshold}.", _options.Rag.SummaryThreshold);
        }

        return ordered;
    }

    private static List<ContextMessage> TrimByThreshold(List<ContextMessage> messages, int threshold)
    {
        if (threshold <= 0)
        {
            return messages;
        }

        return messages
            .Select(message =>
            {
                if (message.Text.Length <= threshold)
                {
                    return message;
                }

                return new ContextMessage
                {
                    Id = message.Id,
                    Author = message.Author,
                    Timestamp = message.Timestamp,
                    Text = message.Text[..threshold],
                    RelevanceScore = message.RelevanceScore
                };
            })
            .ToList();
    }

    private MemoryCacheEntryOptions BuildCacheEntryOptions()
    {
        var ttl = _options.CacheTtl > TimeSpan.Zero ? _options.CacheTtl : TimeSpan.FromMinutes(10);
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };
    }

    private static ContextMessage Clone(ContextMessage message)
    {
        return new ContextMessage
        {
            Id = message.Id,
            Author = message.Author,
            Timestamp = message.Timestamp,
            Text = message.Text,
            RelevanceScore = message.RelevanceScore
        };
    }
}
