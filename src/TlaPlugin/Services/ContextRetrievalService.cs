using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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

    public ContextRetrievalService(
        ITeamsMessageClient teamsClient,
        IMemoryCache cache,
        ITokenBroker tokenBroker,
        IOptions<PluginOptions>? options = null)
    {
        _teamsClient = teamsClient ?? throw new ArgumentNullException(nameof(teamsClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tokenBroker = tokenBroker ?? throw new ArgumentNullException(nameof(tokenBroker));
        _options = options?.Value ?? new PluginOptions();
        _locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ContextRetrievalResult> GetContextAsync(ContextRetrievalRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            return new ContextRetrievalResult();
        }

        var cacheKey = BuildChannelKey(request.TenantId, request.ThreadId ?? request.ChannelId ?? string.Empty);
        var hints = request.ContextHints ?? new List<string>();
        var candidates = await GetOrFetchMessagesAsync(cacheKey, request, cancellationToken).ConfigureAwait(false);

        var messages = ApplyFilters(candidates, hints, request.MaxMessages);

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
            return cached;
        }

        var refreshLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            var limit = request.MaxMessages ?? _options.Rag.MaxMessages;
            if (limit <= 0)
            {
                limit = _options.Rag.MaxMessages > 0 ? _options.Rag.MaxMessages : 10;
            }

            if (string.IsNullOrWhiteSpace(request.UserAssertion))
            {
                return Array.Empty<ContextMessage>();
            }

            AccessToken? accessToken = null;
            try
            {
                accessToken = await _tokenBroker
                    .ExchangeOnBehalfOfAsync(request.TenantId, request.UserId, request.UserAssertion, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AuthenticationException)
            {
                return Array.Empty<ContextMessage>();
            }

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

            var snapshot = fetched
                .Select(Clone)
                .OrderByDescending(message => message.Timestamp)
                .ToList()
                .AsReadOnly();

            _cache.Set(cacheKey, snapshot, BuildCacheEntryOptions());
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
            filtered = filtered.Where(message => hints.Any(hint =>
                message.Text.Contains(hint, StringComparison.OrdinalIgnoreCase)
                || message.Author.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        }

        var limit = overrideLimit ?? _options.Rag.MaxMessages;
        if (limit > 0)
        {
            filtered = filtered.Take(limit);
        }

        var ordered = filtered
            .Select(Clone)
            .ToList();

        if (_options.Rag.SummaryThreshold > 0)
        {
            ordered = TrimByThreshold(ordered, _options.Rag.SummaryThreshold);
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
