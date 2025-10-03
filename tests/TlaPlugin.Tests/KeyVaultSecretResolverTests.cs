using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class KeyVaultSecretResolverTests
{
    [Fact]
    public async Task ReturnsSeedSecret()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "value" }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var value = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);

        Assert.Equal("value", value);
    }

    [Fact]
    public async Task ResolvesMultipleWellKnownSecrets()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["openai-api-key"] = "openai-secret",
                    ["tla-client-secret"] = "client-secret",
                    ["enterprise-graph-secret"] = "enterprise-secret"
                }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var openAi = await resolver.GetSecretAsync("openai-api-key", CancellationToken.None);
        var client = await resolver.GetSecretAsync("tla-client-secret", CancellationToken.None);
        var enterprise = await resolver.GetSecretAsync("enterprise-graph-secret", CancellationToken.None);

        Assert.Equal("openai-secret", openAi);
        Assert.Equal("client-secret", client);
        Assert.Equal("enterprise-secret", enterprise);
    }

    [Fact]
    public async Task UsesCacheUntilInvalidated()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "value" },
                SecretCacheTtl = TimeSpan.FromMinutes(5)
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var first = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);
        options.Value.Security.SeedSecrets["custom-secret"] = "updated";
        var cached = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);

        Assert.Equal(first, cached);

        resolver.Invalidate("custom-secret");
        var refreshed = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);
        Assert.Equal("updated", refreshed);
    }

    [Fact]
    public async Task MaintainsSeparateCachePerTenant()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["shared-secret"] = "tenant-a-value"
                },
                SecretCacheTtl = TimeSpan.FromMinutes(10)
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var tenantAValue = await resolver.GetSecretAsync("shared-secret", "tenant-a", CancellationToken.None);
        Assert.Equal("tenant-a-value", tenantAValue);

        options.Value.Security.SeedSecrets["shared-secret"] = "tenant-b-value";

        var tenantBValue = await resolver.GetSecretAsync("shared-secret", "tenant-b", CancellationToken.None);
        var tenantAFromCache = await resolver.GetSecretAsync("shared-secret", "tenant-a", CancellationToken.None);

        Assert.Equal("tenant-b-value", tenantBValue);
        Assert.Equal("tenant-a-value", tenantAFromCache);
    }

    [Fact]
    public async Task ThrowsWhenSecretMissing()
    {
        var resolver = new KeyVaultSecretResolver();

        await Assert.ThrowsAsync<SecretRetrievalException>(() => resolver.GetSecretAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task FallsBackToSeedWhenVaultUnavailable()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                KeyVaultUri = "https://contoso.vault.azure.net/",
                UseHmacFallback = true,
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "seed-value" }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var value = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);

        Assert.Equal("seed-value", value);
    }

    [Fact]
    public async Task ThrowsWhenHmacFallbackDisabled()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                KeyVaultUri = "https://contoso.vault.azure.net/",
                UseHmacFallback = false,
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "seed-value" }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);

        await Assert.ThrowsAsync<SecretRetrievalException>(() => resolver.GetSecretAsync("custom-secret", CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenRequireVaultSecretsEnabled()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                KeyVaultUri = "https://contoso.vault.azure.net/",
                RequireVaultSecrets = true,
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "seed-value" }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);

        await Assert.ThrowsAsync<SecretRetrievalException>(() => resolver.GetSecretAsync("custom-secret", CancellationToken.None));
    }
}
