using System.Collections.Generic;
using TlaPlugin.Configuration;

namespace TlaPlugin.Models;

/// <summary>
/// 提供给前端的配置摘要模型。
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
    int GlossaryEntryCount,
    TenantPolicySummary TenantPolicies);

/// <summary>
/// 面向前端的模型提供方摘要。
/// </summary>
public record ModelProviderSummary(
    string Id,
    ModelProviderKind Kind,
    double Reliability,
    decimal CostPerCharUsd,
    int LatencyTargetMs,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Certifications);

/// <summary>
/// 租户术语与禁译策略摘要。
/// </summary>
public record TenantPolicySummary(
    string TenantId,
    string GlossaryFallbackPolicy,
    bool EnforceTenantGlossary,
    IReadOnlyList<string> BannedTerms,
    IReadOnlyList<string> StyleTemplates);
