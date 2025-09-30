using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// Teams チャネルから最近のメッセージを取得し、RAG 用コンテキストを提供するサービス。
/// </summary>
public class ContextRetrievalService
{
    private readonly IDictionary<string, IList<ContextMessage>> _messages;
    private readonly PluginOptions _options;

    public ContextRetrievalService(IOptions<PluginOptions>? options = null)
    {
        _options = options?.Value ?? new PluginOptions();
        _messages = new Dictionary<string, IList<ContextMessage>>(StringComparer.OrdinalIgnoreCase);
    }

    public Task<ContextRetrievalResult> GetContextAsync(ContextRetrievalRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var key = BuildChannelKey(request.TenantId, request.ChannelId ?? request.ThreadId ?? string.Empty);
        var candidates = _messages.TryGetValue(key, out var stored)
            ? stored
            : Array.Empty<ContextMessage>();

        var hints = request.ContextHints ?? new List<string>();

        IEnumerable<ContextMessage> filtered = candidates
            .OrderByDescending(message => message.Timestamp);

        if (hints.Count > 0)
        {
            filtered = filtered.Where(message => hints
                .Any(hint => message.Text.Contains(hint, StringComparison.OrdinalIgnoreCase)));
        }

        var limit = request.MaxMessages ?? _options.Rag.MaxMessages;
        if (limit > 0)
        {
            filtered = filtered.Take(limit);
        }

        var result = new ContextRetrievalResult
        {
            Messages = filtered.ToList()
        };

        return Task.FromResult(result);
    }

    public void SeedMessages(string tenantId, string? channelId, IEnumerable<ContextMessage> messages)
    {
        if (tenantId is null)
        {
            throw new ArgumentNullException(nameof(tenantId));
        }

        var key = BuildChannelKey(tenantId, channelId ?? string.Empty);
        _messages[key] = messages.ToList();
    }

    private static string BuildChannelKey(string tenantId, string channelId)
    {
        return $"{tenantId}:{channelId}";
    }
}
