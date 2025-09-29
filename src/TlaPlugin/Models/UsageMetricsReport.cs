using System;
using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// 表示每个模型的使用统计。
/// </summary>
public record ModelUsageSnapshot(string ModelId, int Translations, decimal TotalCostUsd);

/// <summary>
/// 表示单个租户的使用统计快照。
/// </summary>
public record UsageMetricsSnapshot(string TenantId, int Translations, decimal TotalCostUsd, double AverageLatencyMs, DateTimeOffset LastUpdated, IReadOnlyList<ModelUsageSnapshot> Models);

/// <summary>
/// 表示总体与分租户的使用情况汇总。
/// </summary>
public record UsageMetricsOverview(int Translations, decimal TotalCostUsd, double AverageLatencyMs);

public record UsageMetricsReport(UsageMetricsOverview Overall, IReadOnlyList<UsageMetricsSnapshot> Tenants);
