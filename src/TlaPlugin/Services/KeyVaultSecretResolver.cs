using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
<<<<<<< HEAD
/// 从 Key Vault 拉取机密的轻量级解析器。
=======
/// Key Vault からシークレットを取得する簡易リゾルバー。
>>>>>>> origin/main
/// </summary>
public class KeyVaultSecretResolver
{
    private readonly PluginOptions _options;
    private readonly ConcurrentDictionary<string, CachedSecret> _cache = new();

    public KeyVaultSecretResolver(IOptions<PluginOptions>? options = null)
    {
        _options = options?.Value ?? new PluginOptions();
    }

    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(secretName))
        {
            return Task.FromResult(string.Empty);
        }

        if (_cache.TryGetValue(secretName, out var cached) && cached.Expiry > DateTimeOffset.UtcNow)
        {
            return Task.FromResult(cached.Value);
        }

        if (!_options.Security.SeedSecrets.TryGetValue(secretName, out var value))
        {
<<<<<<< HEAD
            throw new InvalidOperationException($"KeyVault 中不存在名为 {secretName} 的机密。");
=======
            throw new InvalidOperationException($"KeyVault にシークレット {secretName} が存在しません。");
>>>>>>> origin/main
        }

        var ttl = _options.Security.SecretCacheTtl <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : _options.Security.SecretCacheTtl;
        var expiry = DateTimeOffset.UtcNow.Add(ttl);
        _cache[secretName] = new CachedSecret(value, expiry);
        return Task.FromResult(value);
    }

    public void Invalidate(string secretName)
    {
        _cache.TryRemove(secretName, out _);
    }

    private readonly record struct CachedSecret(string Value, DateTimeOffset Expiry);
}
