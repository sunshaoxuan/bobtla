using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 模拟 OBO 流程并颁发访问令牌的代理。
/// </summary>
public class TokenBroker : ITokenBroker
{
    private readonly KeyVaultSecretResolver _resolver;
    private readonly PluginOptions _options;
    private readonly IOnBehalfOfTokenClient _onBehalfOfClient;
    private readonly ILogger<TokenBroker>? _logger;
    private readonly ConcurrentDictionary<string, AccessToken> _cache = new();

    public TokenBroker(
        KeyVaultSecretResolver resolver,
        IOptions<PluginOptions>? options = null,
        IOnBehalfOfTokenClient? onBehalfOfClient = null,
        ILogger<TokenBroker>? logger = null)
    {
        _resolver = resolver;
        _options = options?.Value ?? new PluginOptions();
        _onBehalfOfClient = onBehalfOfClient ?? new MsalOnBehalfOfTokenClient();
        _logger = logger;
    }

public async Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, string? userAssertion, CancellationToken cancellationToken)
    {
        var security = _options.Security ?? new SecurityOptions();

        TenantSecurityOverride? tenantOverride = null;
        if (security.TenantOverrides is not null
            && security.TenantOverrides.TryGetValue(tenantId, out var overrideOption))
        {
            tenantOverride = overrideOption;
        }

        var clientSecretName = !string.IsNullOrWhiteSpace(tenantOverride?.ClientSecretName)
            ? tenantOverride!.ClientSecretName!
            : security.ClientSecretName;

        var cacheKey = $"{tenantId}:{clientSecretName}:{userId}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            return cached;
        }

        var clientSecret = await _resolver.GetSecretAsync(clientSecretName, tenantId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new AuthenticationException("无法获取客户端机密。");
        }

        var audience = !string.IsNullOrWhiteSpace(tenantOverride?.UserAssertionAudience)
            ? tenantOverride!.UserAssertionAudience!
            : security.UserAssertionAudience;

        var effectiveClientId = !string.IsNullOrWhiteSpace(tenantOverride?.ClientId)
            ? tenantOverride!.ClientId!
            : security.ClientId;

        var scopes = NormalizeScopes(security.GraphScopes);
        var useMsal = !security.UseHmacFallback && scopes.Length > 0;

        if (useMsal)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new AuthenticationException("缺少用户断言，无法执行 OBO 交换。");
            }

            if (string.IsNullOrWhiteSpace(userAssertion))
            {
                throw new AuthenticationException("缺少用户断言，无法执行 OBO 交换。");
            }

            try
            {
                var result = await _onBehalfOfClient
                    .AcquireTokenAsync(tenantId, effectiveClientId, clientSecret, userAssertion, scopes, cancellationToken)
                    .ConfigureAwait(false);
                var token = new AccessToken(result.AccessToken, result.ExpiresOn, audience);
                _cache[cacheKey] = token;
                return token;
            }
            catch (MsalException ex)
            {
                _logger?.LogError(ex, "OBO 访问令牌获取失败 (tenant: {TenantId}).", tenantId);
                throw new AuthenticationException("OBO 访问令牌获取失败。", ex);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "OBO 访问令牌获取失败 (tenant: {TenantId}).", tenantId);
                throw new AuthenticationException("OBO 访问令牌获取失败。", ex);
            }
        }

        var lifetime = security.AccessTokenLifetime <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(30)
            : security.AccessTokenLifetime;
        var expiresOn = DateTimeOffset.UtcNow.Add(lifetime);
        var value = GenerateToken(tenantId, userId, clientSecret, expiresOn);
        var fallbackToken = new AccessToken(value, expiresOn, audience);
        _cache[cacheKey] = fallbackToken;
        return fallbackToken;
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
        {
            return Array.Empty<string>();
        }

        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            var trimmed = scope.Trim();
            if (seen.Add(trimmed))
            {
                unique.Add(trimmed);
            }
        }

        return unique.ToArray();
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
    Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, string? userAssertion, CancellationToken cancellationToken);
}

public record AccessToken(string Value, DateTimeOffset ExpiresOn, string Audience);
