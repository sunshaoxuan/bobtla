using System;
using System.Collections.Concurrent;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 负责追踪翻译成本并守护租户日预算。
/// </summary>
public class BudgetGuard
{
    private readonly PluginOptions _options;
    private readonly ConcurrentDictionary<string, decimal> _dailyCosts = new();

    public BudgetGuard(PluginOptions? options = null)
    {
        _options = options ?? new PluginOptions();
    }

    public bool TryReserve(string tenantId, decimal cost)
    {
        var key = $"{tenantId}:{DateTime.UtcNow:yyyyMMdd}";
        var total = _dailyCosts.AddOrUpdate(key, cost, (_, existing) => existing + cost);
        if (total > _options.DailyBudgetUsd)
        {
            _dailyCosts.AddOrUpdate(key, 0, (_, existing) => existing - cost);
            return false;
        }
        return true;
    }
}
