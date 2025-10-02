using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TlaPlugin.Models;

/// <summary>
/// 表示翻译输入参数的数据传输对象。
/// </summary>
public class TranslationRequest
{
    public const string DefaultTone = "polite";

    public string Text { get; set; } = string.Empty;
    public string? SourceLanguage { get; set; }
        = null;
    public string TargetLanguage { get; set; } = "ja";
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
    public string? ThreadId { get; set; }
        = null;
    public string Tone { get; set; } = DefaultTone;
    public bool UseGlossary { get; set; } = true;
    public bool UseRag { get; set; } = false;
    public IList<string> AdditionalTargetLanguages { get; set; } = new List<string>();
    public IList<string> ContextHints { get; set; } = new List<string>();
    public string? ContextSummary { get; set; }
        = null;
    public string? UiLocale { get; set; }
        = null;
    public string? UserAssertion { get; set; }
        = null;
    public IDictionary<string, GlossaryDecision> GlossaryDecisions { get; set; }
        = new Dictionary<string, GlossaryDecision>(StringComparer.OrdinalIgnoreCase);

    [JsonExtensionData]
    public IDictionary<string, JsonElement> ExtensionData { get; set; }
        = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}
