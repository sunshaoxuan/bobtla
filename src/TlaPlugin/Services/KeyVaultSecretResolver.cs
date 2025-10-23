using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 从 Key Vault 拉取机密的轻量级解析器。
/// </summary>
public class KeyVaultSecretResolver
{
    private readonly PluginOptions _options;
    private readonly ConcurrentDictionary<string, CachedSecret> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SecretClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly TokenCredential _credential;

    public KeyVaultSecretResolver(IOptions<PluginOptions>? options = null, TokenCredential? credential = null)
    {
        _options = options?.Value ?? new PluginOptions();
        _credential = credential ?? new DefaultAzureCredential();
    }

    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken)
        => GetSecretAsync(secretName, tenantId: null, cancellationToken);

    public async Task<string> GetSecretAsync(string secretName, string? tenantId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSecretSnapshotAsync(secretName, tenantId, cancellationToken).ConfigureAwait(false);
        return snapshot.Value;
    }

    public async Task<SecretValueSnapshot> GetSecretSnapshotAsync(
        string secretName,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(secretName))
        {
            return new SecretValueSnapshot(secretName, tenantId, string.Empty, null, SecretSource.Unknown);
        }

        var cacheKey = BuildCacheKey(secretName, tenantId);
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.CacheExpiry > DateTimeOffset.UtcNow)
        {
            return new SecretValueSnapshot(secretName, tenantId, cached.Value, cached.SecretExpiryUtc, cached.Source);
        }

        var security = _options.Security;
        var (vaultUri, shouldQueryVault) = ResolveVaultUri(tenantId);
        var requireVaultSecrets = security.FailOnSeedFallback || security.RequireVaultSecrets;
        var failOnSeedFallback = shouldQueryVault && (!security.UseHmacFallback || requireVaultSecrets);
        string? resolved = null;
        DateTimeOffset? secretExpiry = null;
        var source = SecretSource.Seed;
        Exception? vaultError = null;

        if (shouldQueryVault)
        {
            try
            {
                var client = GetSecretClient(vaultUri!);
                var response = await client.GetSecretAsync(secretName, cancellationToken: cancellationToken).ConfigureAwait(false);
                resolved = response.Value.Value;
                secretExpiry = response.Value.Properties.ExpiresOn;
                source = SecretSource.KeyVault;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                vaultError = ex;
            }
            catch (Exception ex) when (ex is CredentialUnavailableException or AuthenticationFailedException)
            {
                vaultError = ex;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                vaultError = ex;
            }
        }

        if (string.IsNullOrEmpty(resolved))
        {
            if (failOnSeedFallback)
            {
                var failureReason = vaultError ?? new InvalidOperationException($"KeyVault 中不存在名为 {secretName} 的机密。");
                throw new SecretRetrievalException(secretName, vaultUri, failureReason);
            }

            if (!security.SeedSecrets.TryGetValue(secretName, out resolved) || string.IsNullOrEmpty(resolved))
            {
                if (shouldQueryVault)
                {
                    throw new SecretRetrievalException(secretName, vaultUri, vaultError);
                }

                throw new InvalidOperationException($"KeyVault 中不存在名为 {secretName} 的机密。");
            }

            secretExpiry = null;
            source = SecretSource.Seed;
        }

        var ttl = security.SecretCacheTtl <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : security.SecretCacheTtl;
        var cacheExpiry = DateTimeOffset.UtcNow.Add(ttl);
        _cache[cacheKey] = new CachedSecret(resolved, cacheExpiry, secretExpiry, source);
        return new SecretValueSnapshot(secretName, tenantId, resolved, secretExpiry, source);
    }

    public void Invalidate(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return;
        }

        foreach (var key in _cache.Keys)
        {
            if (key.EndsWith($"::{secretName}", StringComparison.OrdinalIgnoreCase))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    public void Invalidate(string secretName, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return;
        }

        var cacheKey = BuildCacheKey(secretName, tenantId);
        _cache.TryRemove(cacheKey, out _);
    }

    private readonly record struct CachedSecret(string Value, DateTimeOffset CacheExpiry, DateTimeOffset? SecretExpiryUtc, SecretSource Source);

    private static string BuildCacheKey(string secretName, string? tenantId)
        => $"{tenantId ?? "__default__"}::{secretName}";

    private (string? VaultUri, bool Enabled) ResolveVaultUri(string? tenantId)
    {
        var security = _options.Security;
        string? vaultUri = null;

        if (!string.IsNullOrWhiteSpace(tenantId)
            && security.TenantOverrides.TryGetValue(tenantId, out var tenantOverride)
            && !string.IsNullOrWhiteSpace(tenantOverride.KeyVaultUri))
        {
            vaultUri = tenantOverride.KeyVaultUri;
        }

        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            vaultUri = security.KeyVaultUri;
        }

        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            return (null, false);
        }

        if (Uri.TryCreate(vaultUri, UriKind.Absolute, out var parsed))
        {
            var host = parsed.Host;
            var vaultName = host.Split('.')[0];
            if (string.Equals(vaultName, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return (null, false);
            }
        }

        return (vaultUri, true);
    }

    private SecretClient GetSecretClient(string vaultUri)
    {
        return _clients.GetOrAdd(vaultUri, uri =>
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                throw new InvalidOperationException($"KeyVault URI {uri} 无效。");
            }

            return new SecretClient(parsed, _credential);
        });
    }
}

public class SecretRetrievalException : Exception
{
    public SecretRetrievalException(string secretName, string? vaultUri, Exception? innerException)
        : base(CreateMessage(secretName, vaultUri, innerException), innerException)
    {
        SecretName = secretName;
        VaultUri = vaultUri;
    }

    public string SecretName { get; }

    public string? VaultUri { get; }

    private static string CreateMessage(string secretName, string? vaultUri, Exception? innerException)
    {
        var target = string.IsNullOrWhiteSpace(vaultUri) ? "配置的 Key Vault" : vaultUri;
        var reason = innerException?.Message;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return $"无法从 {target} 获取名为 {secretName} 的机密。";
        }

        return $"无法从 {target} 获取名为 {secretName} 的机密: {reason}";
    }
}

public enum SecretSource
{
    Unknown,
    Seed,
    KeyVault
}

public readonly record struct SecretValueSnapshot(
    string Name,
    string? TenantId,
    string Value,
    DateTimeOffset? ExpiresOnUtc,
    SecretSource Source);
