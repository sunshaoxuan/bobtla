using System;

namespace TlaPlugin.Models;

/// <summary>
/// 表示 SQLite 中保存的草稿记录。
/// </summary>
public class OfflineDraftRecord
{
    public long Id { get; set; }
        = 0;
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "PENDING";
    public string? ResultText { get; set; }
        = null;
    public string? ErrorReason { get; set; }
        = null;
    public int Attempts { get; set; }
        = 0;
    public DateTimeOffset? CompletedAt { get; set; }
        = null;
    public string? JobId { get; set; }
        = null;
    public int SegmentIndex { get; set; }
        = 0;
    public int SegmentCount { get; set; }
        = 1;
    public string? AggregatedResult { get; set; }
        = null;
}

public static class OfflineDraftStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
