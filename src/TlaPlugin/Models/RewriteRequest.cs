namespace TlaPlugin.Models;

/// <summary>
/// 表示用户提交的译文润色请求。
/// </summary>
public class RewriteRequest
{
    public string Text { get; set; } = string.Empty;

    public string Tone { get; set; } = TranslationRequest.DefaultTone;

    public string TenantId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string? UiLocale { get; set; }
        = null;
}
