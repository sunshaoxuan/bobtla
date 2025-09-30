namespace TlaPlugin.Models;

/// <summary>
/// 回复线程请求中的语言策略。
/// </summary>
public class ReplyLanguagePolicy
{
    public string TargetLang { get; set; } = string.Empty;
    public string Tone { get; set; } = TranslationRequest.DefaultTone;
}

/// <summary>
/// 表示 /reply 接口的请求体。
/// </summary>
public class ReplyRequest
{
    public string ThreadId { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
    public ReplyLanguagePolicy? LanguagePolicy { get; set; }
        = null;
}

/// <summary>
/// 回复请求的响应。
/// </summary>
public record ReplyResult(string MessageId, string Status, string? FinalText = null, string? ToneApplied = null);
