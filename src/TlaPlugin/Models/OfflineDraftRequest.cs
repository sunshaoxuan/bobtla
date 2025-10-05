namespace TlaPlugin.Models;

/// <summary>
/// 表示保存离线草稿的请求参数。
/// </summary>
public class OfflineDraftRequest
{
    public string OriginalText { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = "ja";
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? JobId { get; set; }
        = null;
    public int SegmentIndex { get; set; }
        = 0;
    public int SegmentCount { get; set; }
        = 1;
}
