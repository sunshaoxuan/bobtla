using TlaPlugin.Configuration;

namespace TlaPlugin.Models;

/// <summary>
/// フロントエンドに提供する構成サマリー。
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
/// モデル提供者情報のフロントエンド向け要約。
/// </summary>
public record ModelProviderSummary(
    string Id,
    ModelProviderKind Kind,
    double Reliability,
    decimal CostPerCharUsd,
    int LatencyTargetMs,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Certifications);
