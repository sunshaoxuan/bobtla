namespace TlaPlugin.Models;

/// <summary>
/// 表示改写调用的请求体。
/// </summary>
public class RewriteRequest
{
    public string Text { get; set; } = string.Empty;
    public string? EditedText { get; set; }
        = null;
    public string Tone { get; set; } = TranslationRequest.DefaultTone;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
}

/// <summary>
/// 改写操作返回的结果。
/// </summary>
public record RewriteResult(string RewrittenText, string ModelId, decimal CostUsd, int LatencyMs);
