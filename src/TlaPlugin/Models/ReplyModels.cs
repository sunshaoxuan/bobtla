using System.Collections.Generic;

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
    private IList<string> _additionalTargetLanguages = new List<string>();

    public string ThreadId { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? EditedText { get; set; }
        = null;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
    public string Language { get; set; } = "auto";
    public string? UiLocale { get; set; }
        = null;
    public ReplyLanguagePolicy? LanguagePolicy { get; set; }
        = null;
    public string? UserAssertion { get; set; }
        = null;
    public bool BroadcastAdditionalLanguages { get; set; }
        = false;

    public IList<string> AdditionalTargetLanguages
    {
        get => _additionalTargetLanguages;
        set => _additionalTargetLanguages = value ?? new List<string>();
    }
}

/// <summary>
/// 回复请求的响应。
/// </summary>
public class ReplyResult
{
    public ReplyResult(string messageId, string status, string? finalText = null, string? toneApplied = null)
    {
        MessageId = messageId;
        Status = status;
        FinalText = finalText;
        ToneApplied = toneApplied;
        PostedAt = DateTimeOffset.UtcNow;
        Dispatches = new List<ReplyDispatch>();
    }

    public string MessageId { get; }

    public string Status { get; }

    public string? FinalText { get; }

    public string? ToneApplied { get; }

    public string Language { get; init; } = string.Empty;

    public DateTimeOffset PostedAt { get; init; }
        = DateTimeOffset.UtcNow;

    public IList<ReplyDispatch> Dispatches { get; init; }
        = new List<ReplyDispatch>();
}

public record ReplyDispatch(
    string MessageId,
    string Language,
    string Status,
    DateTimeOffset PostedAt,
    string? ModelId = null,
    decimal? CostUsd = null,
    int? LatencyMs = null);
