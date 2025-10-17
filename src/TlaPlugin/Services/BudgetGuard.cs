using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 负责追踪翻译成本并守护租户日预算。
/// </summary>
public class BudgetGuard
{
    private readonly PluginOptions _options;
    private readonly ILogger<BudgetGuard>? _logger;
    private readonly ConcurrentDictionary<string, decimal> _dailyCosts = new();

    public BudgetGuard(PluginOptions? options = null, ILogger<BudgetGuard>? logger = null)
    {
        _options = options ?? new PluginOptions();
        _logger = logger;
    }

    public bool TryReserve(string tenantId, decimal cost, out BudgetReservation reservation)
    {
        var key = $"{tenantId}:{DateTime.UtcNow:yyyyMMdd}";
        var total = _dailyCosts.AddOrUpdate(key, cost, (_, existing) => existing + cost);
        _logger?.LogDebug("BudgetGuard reservation attempt for {TenantId} with cost {CostUsd}. New total: {TotalUsd}. Limit: {LimitUsd}.", tenantId, cost, total, _options.DailyBudgetUsd);
        if (total > _options.DailyBudgetUsd)
        {
            _dailyCosts.AddOrUpdate(key, 0, (_, existing) => existing - cost);
            _logger?.LogWarning("BudgetGuard rejected reservation for {TenantId}. Total {TotalUsd} exceeds limit {LimitUsd}.", tenantId, total, _options.DailyBudgetUsd);
            reservation = default!;
            return false;
        }

        reservation = new BudgetReservation(this, key, cost);
        _logger?.LogInformation("BudgetGuard approved reservation for {TenantId}. Cost {CostUsd}, total {TotalUsd} of {LimitUsd}.", tenantId, cost, total, _options.DailyBudgetUsd);
        return true;
    }

    internal void Release(string key, decimal cost)
    {
        _logger?.LogDebug("BudgetGuard releasing {CostUsd} for {Key}.", cost, key);
        _dailyCosts.AddOrUpdate(key, 0, (_, existing) =>
        {
            var updated = existing - cost;
            _logger?.LogTrace("BudgetGuard updated running total for {Key} to {TotalUsd}.", key, updated);
            return updated < 0 ? 0 : updated;
        });
    }

    public sealed class BudgetReservation : IDisposable
    {
        private readonly BudgetGuard _guard;
        private readonly string _key;
        private readonly decimal _cost;
        private bool _disposed;
        private bool _committed;

        internal BudgetReservation(BudgetGuard guard, string key, decimal cost)
        {
            _guard = guard;
            _key = key;
            _cost = cost;
        }

        public void Commit()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BudgetReservation));
            }

            _committed = true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!_committed)
            {
                _guard.Release(_key, _cost);
            }

            _disposed = true;
        }
    }
}
