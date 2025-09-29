namespace TlaPlugin.Models;

/// <summary>
/// 提供给前端的配置摘要。
/// </summary>
public record ConfigurationSummary(
    int MaxCharactersPerRequest,
    decimal DailyBudgetUsd,
    int RequestsPerMinute,
    int MaxConcurrentTranslations,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> DefaultTargetLanguages,
    IReadOnlyDictionary<string, string> ToneTemplates,
    IReadOnlyList<ModelProviderSummary> Providers,
    int GlossaryEntryCount);

/// <summary>
/// 面向前端的模型提供方概要信息。
/// </summary>
public record ModelProviderSummary(
    string Id,
    ModelProviderKind Kind,
    double Reliability,
    decimal CostPerCharUsd,
    int LatencyTargetMs,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Certifications);
