using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// Aggregated metadata exposed to the Teams front-end.
/// </summary>
public record MetadataResponse(
    IReadOnlyList<ModelMetadata> Models,
    IReadOnlyList<LanguageMetadata> Languages,
    MetadataFeatureFlags Features,
    MetadataPricing Pricing);

/// <summary>
/// Model summary exposed to the front-end.
/// </summary>
public record ModelMetadata(
    string Id,
    string DisplayName,
    decimal CostPerCharUsd,
    int LatencyTargetMs);

/// <summary>
/// Language metadata describing supported target languages.
/// </summary>
public record LanguageMetadata(
    string Id,
    string Name,
    bool IsDefault);

/// <summary>
/// Feature toggles that enable or disable optional UX flows.
/// </summary>
public record MetadataFeatureFlags(
    bool TerminologyToggle,
    bool OfflineDraft,
    bool ToneToggle,
    bool Rag);

/// <summary>
/// Pricing details for cost estimation UI.
/// </summary>
public record MetadataPricing(
    string Currency,
    decimal DailyBudgetUsd);
