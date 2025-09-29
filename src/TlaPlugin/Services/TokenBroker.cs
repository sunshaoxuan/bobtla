using System;
using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 模拟 OBO 流程并颁发访问令牌的代理。
/// </summary>
public class TokenBroker : ITokenBroker
{
    private readonly KeyVaultSecretResolver _resolver;
    private readonly PluginOptions _options;
    private readonly ConcurrentDictionary<string, AccessToken> _cache = new();

    public TokenBroker(KeyVaultSecretResolver resolver, IOptions<PluginOptions>? options = null)
    {
        _resolver = resolver;
        _options = options?.Value ?? new PluginOptions();
    }

    public async Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{tenantId}:{userId}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            return cached;
        }

        var clientSecret = await _resolver.GetSecretAsync(_options.Security.ClientSecretName, cancellationToken);
        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new AuthenticationException("无法获取客户端机密。");
        }

        var lifetime = _options.Security.AccessTokenLifetime <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(30)
            : _options.Security.AccessTokenLifetime;
        var expiresOn = DateTimeOffset.UtcNow.Add(lifetime);
        var value = GenerateToken(tenantId, userId, clientSecret, expiresOn);
        var token = new AccessToken(value, expiresOn, _options.Security.UserAssertionAudience);
        _cache[cacheKey] = token;
        return token;
    }

    private static string GenerateToken(string tenantId, string userId, string secret, DateTimeOffset expiresOn)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var payload = $"{tenantId}:{userId}:{expiresOn.ToUnixTimeSeconds()}";
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}:{signature}"));
    }
}

public interface ITokenBroker
{
    Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken);
}

public record AccessToken(string Value, DateTimeOffset ExpiresOn, string Audience);
