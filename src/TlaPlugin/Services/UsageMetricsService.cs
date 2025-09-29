using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 聚合翻译调用的实时使用统计，供前端仪表盘展示成本与延迟。
/// </summary>
public class UsageMetricsService
{
    private readonly ConcurrentDictionary<string, UsageAccumulator> _tenants = new();

    public static class FailureReasons
    {
        public const string Compliance = "合规拒绝";
        public const string Budget = "预算不足";
        public const string Provider = "模型错误";
        public const string Authentication = "认证失败";
    }
    public void RecordSuccess(string tenantId, string modelId, decimal costUsd, int latencyMs, int translationCount)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("租户 ID 不可为空。", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("模型 ID 不可为空。", nameof(modelId));
        }

        if (translationCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(translationCount), "翻译计数必须为正。");
        }

        var accumulator = _tenants.GetOrAdd(tenantId, _ => new UsageAccumulator());
        lock (accumulator.Sync)
        {
            accumulator.Translations += translationCount;
            accumulator.TotalCost += costUsd;
            accumulator.TotalLatency += (long)latencyMs * translationCount;
            accumulator.LastUpdated = DateTimeOffset.UtcNow;

            if (!accumulator.ModelBreakdown.TryGetValue(modelId, out var model))
            {
                model = new ModelAccumulator(modelId);
                accumulator.ModelBreakdown[modelId] = model;
            }

            model.Translations += translationCount;
            model.TotalCost += costUsd;
        }
    }

    public void RecordFailure(string tenantId, string reason)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("租户 ID 不可为空。", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("失败原因不可为空。", nameof(reason));
        }

        var accumulator = _tenants.GetOrAdd(tenantId, _ => new UsageAccumulator());
        lock (accumulator.Sync)
        {
            accumulator.LastUpdated = DateTimeOffset.UtcNow;
            if (!accumulator.Failures.TryGetValue(reason, out var count))
            {
                count = 0;
            }

            accumulator.Failures[reason] = count + 1;
        }
    }
    public UsageMetricsReport GetReport()
    {
        var snapshots = new List<UsageMetricsSnapshot>();
        var overallTranslations = 0;
        decimal overallCost = 0m;
        long overallLatency = 0;
        var failureTotals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in _tenants)
        {
            UsageMetricsSnapshot snapshot;
            lock (entry.Value.Sync)
            {
                snapshot = entry.Value.ToSnapshot(entry.Key);
            }

            snapshots.Add(snapshot);
            overallTranslations += snapshot.Translations;
            overallCost += snapshot.TotalCostUsd;
            overallLatency += (long)Math.Round(snapshot.AverageLatencyMs * snapshot.Translations);

            foreach (var failure in snapshot.Failures)
            {
                failureTotals[failure.Reason] = failureTotals.TryGetValue(failure.Reason, out var count) ? count + failure.Count : failure.Count;
            }
        }

        var ordered = snapshots.OrderByDescending(s => s.LastUpdated).ToList();
        var overallAverage = overallTranslations == 0 ? 0 : (double)overallLatency / overallTranslations;
        var overallFailures = failureTotals
            .Select(pair => new UsageFailureSnapshot(pair.Key, pair.Value))
            .OrderByDescending(snapshot => snapshot.Count)
            .ToList();
        var overall = new UsageMetricsOverview(overallTranslations, decimal.Round(overallCost, 4), Math.Round(overallAverage, 2), overallFailures);
        return new UsageMetricsReport(overall, ordered);
    }

    private sealed class UsageAccumulator
    {
        public object Sync { get; } = new();
        public int Translations { get; set; }
        public decimal TotalCost { get; set; }
        public long TotalLatency { get; set; }
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.MinValue;
        public Dictionary<string, ModelAccumulator> ModelBreakdown { get; } = new();
        public Dictionary<string, int> Failures { get; } = new();

        public UsageMetricsSnapshot ToSnapshot(string tenantId)
        {
            var average = Translations == 0 ? 0 : (double)TotalLatency / Translations;
            var models = ModelBreakdown.Values
                .Select(m => new ModelUsageSnapshot(m.ModelId, m.Translations, decimal.Round(m.TotalCost, 4)))
                .OrderByDescending(m => m.Translations)
                .ToList();
            var failures = Failures
                .Select(pair => new UsageFailureSnapshot(pair.Key, pair.Value))
                .OrderByDescending(snapshot => snapshot.Count)
                .ToList();
            return new UsageMetricsSnapshot(tenantId, Translations, decimal.Round(TotalCost, 4), Math.Round(average, 2), LastUpdated, models, failures);
        }
    }

    private sealed class ModelAccumulator
    {
        public ModelAccumulator(string modelId)
        {
            ModelId = modelId;
        }

        public string ModelId { get; }
        public int Translations { get; set; }
        public decimal TotalCost { get; set; }
    }
}
