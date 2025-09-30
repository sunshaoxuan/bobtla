namespace TlaPlugin.Models;

/// <summary>
/// 表示用户确认后的回帖请求。
/// </summary>
public class ReplyRequest
{
    public string Text { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string? ChannelId { get; set; }
        = null;

    public string Language { get; set; } = "auto";

    public string? UiLocale { get; set; }
        = null;
}
