using System;

namespace TlaPlugin.Models;

/// <summary>
/// 表示回帖操作的结果。
/// </summary>
public class ReplyResult
{
    public string MessageId { get; set; } = string.Empty;

    public string Status { get; set; } = "sent";

    public string Language { get; set; } = string.Empty;

    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
}
