using System;
using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// 表示检索周边上下文的请求参数。
/// </summary>
public class ContextRetrievalRequest
{
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
    public string? ThreadId { get; set; }
        = null;
    public int? MaxMessages { get; set; }
        = null;
    public IList<string> ContextHints { get; set; }
        = new List<string>();
    public string? UserAssertion { get; set; }
        = null;
}

/// <summary>
/// 表示上下文检索命中的一条消息。
/// </summary>
public class ContextMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Author { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
        = DateTimeOffset.UtcNow;
    public string Text { get; set; } = string.Empty;
    public double RelevanceScore { get; set; } = 0d;
}

/// <summary>
/// 表示上下文检索的聚合结果。
/// </summary>
public class ContextRetrievalResult
{
    public IReadOnlyList<ContextMessage> Messages { get; init; }
        = Array.Empty<ContextMessage>();

    public bool HasContext => Messages.Count > 0;
}
