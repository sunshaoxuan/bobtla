using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 抽象化 Teams/Graph SDK，提供从频道检索最近消息的接口。
/// </summary>
public interface ITeamsMessageClient
{
    /// <summary>
    /// 获取指定频道或线程的最新消息集合。
    /// </summary>
    /// <param name="tenantId">目标团队或租户标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="threadId">线程标识，当为 null 时读取频道顶层消息。</param>
    /// <param name="maxMessages">最大返回消息数量。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>按时间降序排列的消息列表。</returns>
    Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
        string tenantId,
        string? channelId,
        string? threadId,
        int maxMessages,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取并允许自定义访问令牌的重载。
    /// </summary>
    /// <param name="tenantId">目标团队或租户标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="threadId">线程标识。</param>
    /// <param name="maxMessages">最大返回消息数量。</param>
    /// <param name="accessToken">Graph 访问令牌。</param>
    /// <param name="userId">与令牌关联的用户标识。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<IReadOnlyList<ContextMessage>> GetRecentMessagesAsync(
        string tenantId,
        string? channelId,
        string? threadId,
        int maxMessages,
        AccessToken? accessToken,
        string? userId,
        CancellationToken cancellationToken)
        => GetRecentMessagesAsync(tenantId, channelId, threadId, maxMessages, cancellationToken);
}
