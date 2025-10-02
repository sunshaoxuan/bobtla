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

    public bool TryReserve(string tenantId, decimal cost, out BudgetReservation reservation)
    {
        var key = $"{tenantId}:{DateTime.UtcNow:yyyyMMdd}";
        var total = _dailyCosts.AddOrUpdate(key, cost, (_, existing) => existing + cost);
        if (total > _options.DailyBudgetUsd)
        {
            _dailyCosts.AddOrUpdate(key, 0, (_, existing) => existing - cost);
            reservation = default!;
            return false;
        }

        reservation = new BudgetReservation(this, key, cost);
        return true;
    }

    internal void Release(string key, decimal cost)
    {
        _dailyCosts.AddOrUpdate(key, 0, (_, existing) =>
        {
            var updated = existing - cost;
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
