using System;

namespace TlaPlugin.Services;

/// <summary>
/// 提供阶段就绪状态的持久化存取能力。
/// </summary>
public interface IStageReadinessStore
{
    /// <summary>
    /// 读取最近一次阶段冒烟成功的时间戳。
    /// </summary>
    /// <returns>若无记录则返回 <c>null</c>。</returns>
    DateTimeOffset? ReadLastSuccess();

    /// <summary>
    /// 写入最近一次阶段冒烟成功的时间戳。
    /// </summary>
    /// <param name="timestamp">最新的成功时间。</param>
    void WriteLastSuccess(DateTimeOffset timestamp);
}
