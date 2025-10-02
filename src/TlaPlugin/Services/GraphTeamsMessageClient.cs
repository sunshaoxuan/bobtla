using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 使用 Microsoft Graph SDK 访问 Teams 消息的默认实现。
/// </summary>
public class GraphTeamsMessageClient : ITeamsMessageClient
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GraphServiceClient _graphClient;
    private readonly IGraphRequestContextAccessor _contextAccessor;

    public GraphTeamsMessageClient(GraphServiceClient graphClient, IGraphRequestContextAccessor contextAccessor)
    {
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
        string tenantId,
        string? channelId,
        string? threadId,
        int maxMessages,
        CancellationToken cancellationToken)
        => GetRecentMessagesAsync(
            tenantId,
            channelId,
            threadId,
            maxMessages,
            accessToken: null,
            userId: null,
            userAssertion: null,
            cancellationToken);

    public async Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
        string tenantId,
        string? channelId,
        string? threadId,
        int maxMessages,
        AccessToken? accessToken,
        string? userId,
        string? userAssertion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(channelId))
        {
            return Array.Empty<ContextMessage>();
        }

        maxMessages = Math.Max(1, maxMessages);

        var messages = new List<ContextMessage>();

        using var scope = _contextAccessor.Push(new GraphRequestContext(tenantId, userId, accessToken, userAssertion));

        var threadLookupAttempted = false;
        if (!string.IsNullOrEmpty(threadId))
        {
            threadLookupAttempted = true;
            try
            {
                var root = await _graphClient.Teams[tenantId].Channels[channelId].Messages[threadId]
                    .GetAsync(static request =>
                    {
                        request.QueryParameters.Select = new[] { "id", "body", "from", "createdDateTime", "lastModifiedDateTime", "replyToId" };
                    }, cancellationToken).ConfigureAwait(false);

                if (root is not null)
                {
                    messages.Add(Convert(root));
                }

                var replies = await _graphClient.Teams[tenantId].Channels[channelId].Messages[threadId].Replies
                    .GetAsync(request =>
                    {
                        request.QueryParameters.Top = maxMessages;
                        request.QueryParameters.Orderby = new[] { "lastModifiedDateTime desc" };
                        request.QueryParameters.Select = new[] { "id", "body", "from", "createdDateTime", "lastModifiedDateTime", "replyToId" };
                    }, cancellationToken).ConfigureAwait(false);

                if (replies?.Value is not null)
                {
                    messages.AddRange(replies.Value.Select(Convert));
                }
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Thread no longer exists; fall back to channel messages below.
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return Array.Empty<ContextMessage>();
            }
        }

        if (messages.Count == 0)
        {
            try
            {
                var response = await _graphClient.Teams[tenantId].Channels[channelId].Messages
                    .GetAsync(request =>
                    {
                        request.QueryParameters.Top = maxMessages;
                        request.QueryParameters.Orderby = new[] { "lastModifiedDateTime desc" };
                        request.QueryParameters.Select = new[] { "id", "body", "from", "createdDateTime", "lastModifiedDateTime", "replyToId" };
                    }, cancellationToken).ConfigureAwait(false);

                if (response?.Value is not null)
                {
                    messages.AddRange(response.Value.Select(Convert));
                }
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return Array.Empty<ContextMessage>();
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound && threadLookupAttempted)
            {
                return Array.Empty<ContextMessage>();
            }
        }

        return messages
            .OrderByDescending(message => message.Timestamp)
            .Take(maxMessages)
            .ToList();
    }

    private static ContextMessage Convert(ChatMessage message)
    {
        var timestamp = message.LastModifiedDateTime ?? message.CreatedDateTime ?? DateTimeOffset.UtcNow;
        var text = message.Body?.Content ?? string.Empty;
        if (message.Body?.ContentType == BodyType.Html)
        {
            text = HtmlTagRegex.Replace(text, string.Empty);
        }

        var author = message.From?.User?.DisplayName
            ?? message.From?.Application?.DisplayName
            ?? message.From?.Device?.DisplayName
            ?? string.Empty;

        return new ContextMessage
        {
            Id = message.Id ?? Guid.NewGuid().ToString("N"),
            Author = author,
            Text = text.Trim(),
            Timestamp = timestamp,
            RelevanceScore = 0d
        };
    }
}
