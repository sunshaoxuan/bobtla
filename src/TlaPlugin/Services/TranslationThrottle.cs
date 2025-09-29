using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 同時実行数とレート制限を統合管理するスロットル。
/// </summary>
public class TranslationThrottle
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _requestsPerMinute;
    private readonly ConcurrentDictionary<string, RateWindow> _rateWindows = new();

    public TranslationThrottle(IOptions<PluginOptions>? options = null)
    {
        var resolved = options?.Value ?? new PluginOptions();
        var concurrency = Math.Max(1, resolved.MaxConcurrentTranslations);
        _requestsPerMinute = resolved.RequestsPerMinute <= 0 ? int.MaxValue : resolved.RequestsPerMinute;
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
    }

    public async Task<IDisposable> AcquireAsync(string tenantId, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        if (!TryIncrement(tenantId))
        {
            _semaphore.Release();
            throw new RateLimitExceededException("1 分あたりのリクエスト上限を超えました。");
        }

        return new Lease(this);
    }

    private bool TryIncrement(string tenantId)
    {
        if (_requestsPerMinute == int.MaxValue)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_rateWindows.TryGetValue(tenantId, out var window))
            {
                if (_rateWindows.TryAdd(tenantId, new RateWindow(now, 1)))
                {
                    return true;
                }
                continue;
            }

            if (now - window.WindowStart >= TimeSpan.FromMinutes(1))
            {
                if (_rateWindows.TryUpdate(tenantId, new RateWindow(now, 1), window))
                {
                    return true;
                }
                continue;
            }

            if (window.Count >= _requestsPerMinute)
            {
                return false;
            }

            if (_rateWindows.TryUpdate(tenantId, window with { Count = window.Count + 1 }, window))
            {
                return true;
            }
        }
    }

    private void Release()
    {
        _semaphore.Release();
    }

    private sealed class Lease : IDisposable
    {
        private readonly TranslationThrottle _owner;
        private bool _disposed;

        public Lease(TranslationThrottle owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Release();
        }
    }

    private record struct RateWindow(DateTimeOffset WindowStart, int Count);
}

public class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message)
    {
    }
}
