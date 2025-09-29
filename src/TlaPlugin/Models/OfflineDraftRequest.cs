namespace TlaPlugin.Models;

/// <summary>
/// 离线草稿的保存请求。
/// </summary>
public class OfflineDraftRequest
{
    public string OriginalText { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = "ja";
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}
