namespace TlaPlugin.Models;

/// <summary>
/// オフライン草稿の保存要求。
/// </summary>
public class OfflineDraftRequest
{
    public string OriginalText { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = "ja";
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}
