namespace TlaPlugin.Models;

/// <summary>
/// SQLite に保存される草稿行。
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
}
