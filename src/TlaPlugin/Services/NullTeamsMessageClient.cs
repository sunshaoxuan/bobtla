using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 返回空结果的占位实现，用于未配置 Graph 客户端的场景。
/// </summary>
public sealed class NullTeamsMessageClient : ITeamsMessageClient
{
    public Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
        string tenantId,
        string? channelId,
        string? threadId,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContextMessage> empty = Array.Empty<ContextMessage>();
        return Task.FromResult(empty);
    }
}
