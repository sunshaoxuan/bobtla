using System.Text.Json.Nodes;

namespace TlaPlugin.Models;

/// <summary>
/// 存放翻译结果的模型。
/// </summary>
public class TranslationResult
{
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public double Confidence { get; set; }
        = 0.0;
    public decimal CostUsd { get; set; }
        = 0m;
    public int LatencyMs { get; set; }
        = 0;
    public JsonObject? AdaptiveCard { get; set; }
        = new JsonObject();
    public IDictionary<string, string> AdditionalTranslations { get; set; }
        = new Dictionary<string, string>();
}
